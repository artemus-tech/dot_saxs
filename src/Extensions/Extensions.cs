// Удобные сокращения поверх основных типов (DECISIONS.md, п. 24): то, что пишется через LINQ в одну строку.
// Основной API остаётся минимальным; сокращения живут здесь — посмотрим, какие пригодятся.
// Появятся вместе с типами на этапе 2. Первое на очереди:
//
//   cube.GetValuesByPosition(row, column)  ==  cube.GetMatrices().Select(m => m[row, column]).ToVector()
namespace DotSaxs;
