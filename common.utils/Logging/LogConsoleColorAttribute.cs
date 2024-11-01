using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace common.utils.Logging;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
sealed class LogConsoleColorAttribute(ConsoleColor background, ConsoleColor foreground) : Attribute
{
  public ConsoleColor Background { get; private set; } = background;
  public ConsoleColor Foreground { get; private set; } = foreground;
}
