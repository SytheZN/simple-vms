using System.Diagnostics.CodeAnalysis;
using Shared.Models.Formats;

namespace Analyzer.MotionGridH26x.Filters;

internal interface IFilter
{
  void Feed(MotionGridUnit unit);
  bool TryReceive([MaybeNullWhen(false)] out MotionGridUnit unit);
  void Flush();
}
