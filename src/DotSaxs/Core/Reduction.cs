using System;
using System.Diagnostics;
using System.Numerics;

namespace DotSaxs.Core;

/// <summary>
/// Как NumPy обходит вид, когда сворачивает его суммой (<c>np.sum</c> — это <c>np.add.reduce</c>): в каком порядке
/// идут оси, какие соседние оси сливаются в одну и сколько чисел за раз уходит во внутренний цикл. От этого зависит
/// порядок сложений, а значит, и последние биты суммы (DECISIONS.md п. 24). Перенос итератора nditer из NumPy v2.5.4
/// для свёртки одного вида без приведения типов: операнд 0 — результат (итератор создаёт его сам), операнд 1 — вход.
/// </summary>
internal readonly struct ReductionPlan
{
    /// <summary>Сколько чисел помещается в буфер обхода.</summary>
    internal const int BufferSize = 8192;  // ref: numpy/_core/include/numpy/ndarraytypes.h#L1072

    // Оси обхода после упорядочивания и слияния, ось 0 — самая внутренняя (AXISDATA у nditer): длина, шаг результата и
    // шаг входа в числах.
    private readonly Dims _shape;
    private readonly Dims _out;
    private readonly Dims _in;

    // Шаг результата по каждой исходной оси вида: NumPy кладёт числа результата в порядке обхода, а не в порядке C.
    private readonly Dims _placement;

    /// <summary>План свёртки вида <paramref name="layout"/> по осям из <paramref name="reduced"/> (бит i — ось i).</summary>
    internal ReductionPlan(in Layout layout, int reduced)
    {
        int nd = layout.Rank;
        if (nd == 0)
            throw new ArgumentOutOfRangeException(nameof(layout));

        // ref: numpy/_core/src/multiarray/nditer_constr.c#L1648 — у оси длины 1 шаг 0
        // ref: numpy/_core/src/multiarray/nditer_constr.c#L1632-L1634 — у результата шаг 0 по осям свёртки
        Span<int> inStride = stackalloc int[Dimensions.Max];
        for (int a = 0; a < nd; a++)
            inStride[a] = layout.Shape(a) == 1 ? 0 : layout.Stride(a);

        // ref: numpy/_core/src/multiarray/nditer_constr.c#L2681-L2746 — устойчивая сортировка вставками: оси идут
        // от меньшего шага к большему, сначала — в обратном порядке C. Шаги результата здесь ещё нули (его нет), и
        // порядок задаёт только вход; шаг 0 сравнения не решает.
        Span<int> perm = stackalloc int[Dimensions.Max];
        for (int i = 0; i < nd; i++)
            perm[i] = nd - 1 - i;
        for (int i0 = 1; i0 < nd; i0++)
        {
            int ipos = i0, j0 = perm[i0];
            for (int i1 = i0 - 1; i1 >= 0; i1--)
            {
                long s0 = inStride[j0], s1 = inStride[perm[i1]];
                if (s0 == 0 || s1 == 0)
                    continue;
                if (Math.Abs(s1) <= Math.Abs(s0))
                    break;
                ipos = i1;
            }
            if (ipos != i0)
            {
                for (int i1 = i0; i1 > ipos; i1--)
                    perm[i1] = perm[i1 - 1];
                perm[ipos] = j0;
            }
        }

        // ref: numpy/_core/src/multiarray/nditer_constr.c#L2928-L2954 — результат итератор создаёт сам, оси — в порядке
        // обхода: по самой внутренней оси числа результата идут подряд
        Span<int> outStride = stackalloc int[Dimensions.Max];
        Dims placement = default;
        int step = 1;
        for (int i = 0; i < nd; i++)
        {
            int a = perm[i];
            bool kept = (reduced & (1 << a)) == 0;
            outStride[a] = kept && layout.Shape(a) != 1 ? step : 0;
            placement[a] = outStride[a];
            if (kept)
                step *= layout.Shape(a);
        }
        int outputSize = 1;
        for (int a = 0; a < nd; a++)
        {
            if ((reduced & (1 << a)) == 0)
                outputSize *= layout.Shape(a);
        }

        // ref: numpy/_core/src/multiarray/nditer_api.c#L1659-L1694 — соседние оси сливаются в одну, если и у результата,
        // и у входа шаг следующей оси — шаг предыдущей, умноженный на её длину (или одна из них длины 1 с шагом 0)
        Dims shape = default, outs = default, ins = default;
        int rank = 0;
        for (int i = 0; i < nd; i++)
        {
            int a = perm[i];
            int n = layout.Shape(a), o = outStride[a], s = inStride[a];
            int c = rank - 1;
            if (rank > 0 && Joins(shape[c], outs[c], n, o) && Joins(shape[c], ins[c], n, s))
            {
                // ref: numpy/_core/src/multiarray/nditer_api.c#L1680-L1684
                shape[c] *= n;
                if (outs[c] == 0)
                    outs[c] = o;
                if (ins[c] == 0)
                    ins[c] = s;
            }
            else
            {
                shape[rank] = n;
                outs[rank] = o;
                ins[rank] = s;
                rank++;
            }
        }

        // ref: numpy/_core/src/multiarray/nditer_constr.c#L2009 — npyiter_find_buffering_setup: до какой оси идёт
        // внутренний цикл (OuterDim), сколько в нём чисел (Core) и сколько чисел за раз копируется в буфер (Transfer).
        // Цена — 1 плюс число операндов, которые идут через буфер; лучшая ось — где цена на число в буфере меньше.
        int cost = 1;
        int singleOut = 1, singleIn = 1;  // сколько первых осей операнд проходит одним шагом
        int outerReduce = 0;              // ось, на которой у результата шаг меняется между 0 и не 0
        long size = shape[0];
        int bestDim = 0, bestCost = 1;
        long bestSize = size, bestCore = 1;
        for (int idim = 1; idim < rank; idim++)
        {
            // ref: numpy/_core/src/multiarray/nditer_constr.c#L2072-L2080 — за ось свёртки не идём, за буфер — тоже,
            // если что-то буферизуется
            if (outerReduce != 0 || (size >= BufferSize && cost > 1))
                break;
            long previous = shape[idim - 1];

            // ref: numpy/_core/src/multiarray/nditer_constr.c#L2097-L2126 — результат
            if (singleOut == idim && outs[idim - 1] * previous == outs[idim])
            {
                singleOut++;
            }
            else
            {
                if (singleOut == idim)
                    cost++;
                if (outs[idim] == 0 || outs[idim - 1] == 0)
                    outerReduce = idim;
            }

            // ref: numpy/_core/src/multiarray/nditer_constr.c#L2097-L2113 — вход
            if (singleIn == idim)
            {
                if (ins[idim - 1] * previous == ins[idim])
                    singleIn++;
                else
                    cost++;
            }

            // ref: numpy/_core/src/multiarray/nditer_constr.c#L2131-L2157
            long core = size;
            size *= shape[idim];
            if (size == 0)
                break;
            double bufferSize = size > BufferSize && cost > 1 ? BufferSize : size;
            if (cost * (double)bestSize <= bestCost * bufferSize)
            {
                bestCost = cost;
                bestCore = core;
                bestSize = size;
                bestDim = idim;
            }
        }

        // ref: numpy/_core/src/multiarray/nditer_constr.c#L2160 — свёртка по внешней оси, если лучшая ось — ось свёртки
        bool usingReduce = outerReduce != 0 && bestDim == outerReduce;

        // ref: numpy/_core/src/multiarray/nditer_constr.c#L2204-L2244 — какой операнд идёт через буфер. Результат в
        // порядке обхода всегда проходит внутренний цикл одним шагом, вход — не всегда.
        bool outReduce = usingReduce;
        bool inReduce = usingReduce && (singleIn == bestDim || (ins[outerReduce] == 0 && singleIn <= bestDim));
        if (singleOut + (outReduce ? 1 : 0) <= bestDim)
            throw new UnreachableException();
        bool inputBuffered = singleIn + (inReduce ? 1 : 0) <= bestDim;

        // ref: numpy/_core/src/multiarray/nditer_constr.c#L2321-L2324 — с буфером: не больше буфера и кратно Core
        if (inputBuffered && BufferSize < bestSize)
            bestSize = bestCore * (BufferSize / bestCore);

        _shape = shape;
        _out = outs;
        _in = ins;
        _placement = placement;
        Rank = rank;
        OuterDim = bestDim;
        Core = (int)bestCore;
        Transfer = (int)bestSize;
        UsingReduce = usingReduce;
        InputBuffered = inputBuffered;
        OutputSize = outputSize;
    }

    /// <summary>Сколько осей обхода после слияния.</summary>
    internal int Rank { get; }

    /// <summary>Внешняя ось: оси ниже неё — внутренний цикл, оси выше — перебор вне буфера.</summary>
    internal int OuterDim { get; }

    /// <summary>Сколько чисел в осях ниже внешней.</summary>
    internal int Core { get; }

    /// <summary>Сколько чисел за раз уходит в буфер (кратно <see cref="Core"/>).</summary>
    internal int Transfer { get; }

    /// <summary>
    /// Свёртка по внешней оси: внутренний цикл идёт по <see cref="Core"/> чисел отдельно для каждого места на внешней
    /// оси. Иначе — по всем числам, переданным в буфер.
    /// </summary>
    internal bool UsingReduce { get; }

    /// <summary>Вход копируется в буфер: одним шагом его числа во внутреннем цикле не пройти.</summary>
    internal bool InputBuffered { get; }

    /// <summary>Сколько чисел в результате.</summary>
    internal int OutputSize { get; }

    /// <summary>Длина оси обхода <paramref name="dim"/>.</summary>
    internal int Shape(int dim) => _shape[dim];

    /// <summary>Шаг результата по оси обхода <paramref name="dim"/>.</summary>
    internal int OutStride(int dim) => _out[dim];

    /// <summary>Шаг входа по оси обхода <paramref name="dim"/>.</summary>
    internal int InStride(int dim) => _in[dim];

    /// <summary>Шаг результата по исходной оси вида <paramref name="axis"/>.</summary>
    internal int Placement(int axis) => _placement[axis];

    // ref: numpy/_core/src/multiarray/nditer_api.c#L1667-L1674
    private static bool Joins(long shape0, long stride0, long shape1, long stride1) =>
        (shape0 == 1 && stride0 == 0) || (shape1 == 1 && stride1 == 0) || stride0 * shape0 == stride1;
}

/// <summary>Сумма вида с любыми шагами в том же порядке сложений, что у NumPy.</summary>
internal static class Reduction
{
    /// <summary>
    /// Сумма вида <paramref name="layout"/> по осям из <paramref name="reduced"/> (бит i — ось i), как
    /// <c>np.sum(a, axis=…)</c>: числа результата — по оставшимся осям в порядке C. Каждая сумма начинается с +0.
    /// </summary>
    internal static T[] Sum<T, TAddition>(T[] data, in Layout layout, int reduced, ref TAddition addition)
        where T : IFloatingPointIeee754<T>
        where TAddition : struct, IAddition<T>
    {
        var plan = new ReductionPlan(layout, reduced);
        // ref: numpy/_core/src/umath/reduction.c#L347 — начальное значение суммы — единица сложения, +0
        var output = new T[plan.OutputSize];
        output.AsSpan().Fill(T.Zero);
        if (layout.Size == 0)
            return output;

        int outer = plan.OuterDim, core = plan.Core;
        int length = plan.Shape(outer), inStep = plan.InStride(outer), outStep = plan.OutStride(outer);
        T[]? buffer = plan.InputBuffered ? new T[plan.UsingReduce ? core : plan.Transfer] : null;
        Span<int> index = stackalloc int[Dimensions.Max];
        int inBase = layout.Offset, outBase = 0;
        while (true)
        {
            // ref: numpy/_core/src/multiarray/nditer_api.c#L2104-L2129 — за раз в буфер идёт не больше Transfer чисел и
            // не дальше конца внешней оси
            for (int o = 0; o < length;)
            {
                int k = Math.Min(plan.Transfer, core * (length - o)) / core;
                if (k == 0)
                    throw new UnreachableException();
                if (plan.UsingReduce)
                {
                    // ref: numpy/_core/src/multiarray/nditer_api.c#L2147-L2153 — внутренний цикл — по Core чисел
                    for (int r = o; r < o + k; r++)
                        Inner(data, inBase + r * inStep, output, outBase + r * outStep, plan, outer, plan.Shape(outer - 1), buffer, ref addition);
                }
                else
                {
                    Inner(data, inBase + o * inStep, output, outBase + o * outStep, plan, outer + 1, k, buffer, ref addition);
                }
                o += k;
            }

            // Следующее место на осях выше внешней: ближайшая к внешней меняется чаще.
            int d = outer + 1;
            for (; d < plan.Rank; d++)
            {
                inBase += plan.InStride(d);
                outBase += plan.OutStride(d);
                if (++index[d] < plan.Shape(d))
                    break;
                inBase -= plan.InStride(d) * plan.Shape(d);
                outBase -= plan.OutStride(d) * plan.Shape(d);
                index[d] = 0;
            }
            if (d >= plan.Rank)
                break;
        }
        return InOrderC(output, plan, layout, reduced);
    }

    /// <summary>
    /// Один вызов внутреннего цикла сложения: числа входа на осях обхода 0 … <paramref name="dims"/> − 1, на последней
    /// из них — первые <paramref name="last"/> чисел. Результат проходит их одним шагом: шаг 0 — всё в одну сумму
    /// (попарно), иначе — число к числу.
    /// </summary>
    private static void Inner<T, TAddition>(
        T[] data, int inStart, T[] output, int outStart, in ReductionPlan plan, int dims, int last, T[]? buffer,
        ref TAddition addition)
        where T : IFloatingPointIeee754<T>
        where TAddition : struct, IAddition<T>
    {
        int count = last;
        for (int d = 0; d < dims - 1; d++)
            count *= plan.Shape(d);
        T[] source = data;
        int start = inStart, stride = plan.InStride(0);
        if (buffer is not null)
        {
            Gather(data, inStart, plan, dims, last, buffer);
            source = buffer;
            start = 0;
            stride = 1;
        }
        int outStride = plan.OutStride(0);
        if (outStride == 0)
        {
            // ref: numpy/_core/src/umath/loops_arithm_fp.dispatch.c.src#L52-L54 — свёртка: к сумме — попарная сумма
            output[outStart] = addition.Add(output[outStart], Pairwise.Sum(source, start, count, stride, ref addition));
            return;
        }
        // ref: numpy/_core/src/umath/loops_arithm_fp.dispatch.c.src#L174-L177 — иначе — число к числу
        for (int i = 0; i < count; i++)
        {
            int p = outStart + i * outStride;
            output[p] = addition.Add(output[p], source[start + i * stride]);
        }
    }

    /// <summary>
    /// Копирует числа входа в буфер в порядке обхода — ось 0 меняется чаще всех, как при копировании в буфер у NumPy
    /// (nditer_api.c, npyiter_copy_to_buffers).
    /// </summary>
    private static void Gather<T>(T[] data, int start, in ReductionPlan plan, int dims, int last, T[] buffer)
    {
        Span<int> index = stackalloc int[Dimensions.Max];
        int first = dims == 1 ? last : plan.Shape(0), stride = plan.InStride(0);
        int p = start, b = 0;
        while (true)
        {
            for (int i = 0; i < first; i++)
                buffer[b++] = data[p + i * stride];
            int d = 1;
            for (; d < dims; d++)
            {
                int length = d == dims - 1 ? last : plan.Shape(d);
                p += plan.InStride(d);
                if (++index[d] < length)
                    break;
                p -= plan.InStride(d) * length;
                index[d] = 0;
            }
            if (d >= dims)
                return;
        }
    }

    /// <summary>
    /// Числа результата — в порядке C по оставшимся осям. NumPy кладёт их в порядке обхода; если он совпадает с
    /// порядком C, массив возвращается как есть.
    /// </summary>
    private static T[] InOrderC<T>(T[] output, in ReductionPlan plan, in Layout layout, int reduced)
    {
        Span<int> axes = stackalloc int[Dimensions.Max];
        int kept = 0;
        for (int a = 0; a < layout.Rank; a++)
        {
            if ((reduced & (1 << a)) == 0)
                axes[kept++] = a;
        }
        bool inOrder = true;
        int expected = 1;
        for (int j = kept - 1; j >= 0; j--)
        {
            int length = layout.Shape(axes[j]);
            if (length > 1 && plan.Placement(axes[j]) != expected)
                inOrder = false;
            expected *= length;
        }
        if (inOrder || output.Length == 0)
            return output;

        var result = new T[output.Length];
        Span<int> index = stackalloc int[Dimensions.Max];
        int p = 0;
        for (int c = 0; c < result.Length; c++)
        {
            result[c] = output[p];
            for (int j = kept - 1; j >= 0; j--)
            {
                int a = axes[j];
                p += plan.Placement(a);
                if (++index[j] < layout.Shape(a))
                    break;
                p -= plan.Placement(a) * layout.Shape(a);
                index[j] = 0;
            }
        }
        return result;
    }
}
