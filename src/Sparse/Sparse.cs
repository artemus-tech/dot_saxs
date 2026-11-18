// Разреженные матрицы (DECISIONS.md, п. 25): один тип SparseMatrix, внутри — формат CSR.
// Создаётся из троек (строка, столбец, значение) или из Matrix; умеет LinAlgMultiply, Solve, Transpose, ToMatrix().
// Основные типы о SparseMatrix не знают: matrix.ToSparse() — метод-расширение из этой сборки.
// Появится на этапе 6.
namespace DotSaxs;
