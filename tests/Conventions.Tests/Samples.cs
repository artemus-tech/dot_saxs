namespace DotSaxs.ConventionsTests.Samples;

/// <summary>Образец без [Original] — чтобы убедиться, что проверка находит нарушения.</summary>
public sealed class Unannotated
{
    /// <summary>Свойство без [Original].</summary>
    public int Value { get; set; }

    /// <summary>Метод без [Original].</summary>
    public void Run()
    {
    }

    /// <summary>Оператор без [Original].</summary>
    public static Unannotated operator +(Unannotated a, Unannotated b) => a;
}

/// <summary>Образец, где [Original] стоит везде, — проверка не должна найти нарушений.</summary>
[Original("numpy.sample", "sample.py", 1)]
public sealed class Annotated
{
    /// <summary>Конструктор с [Original].</summary>
    [Original("numpy.sample.__init__", "sample.py", 2)]
    public Annotated()
    {
    }

    /// <summary>Свойство с [Original].</summary>
    [Original("numpy.sample.value", "sample.py", 3)]
    public int Value { get; set; }

    /// <summary>Метод с [Original].</summary>
    [Original("numpy.sample.run", "sample.py", 4)]
    public void Run()
    {
    }
}
