
namespace common.utils.Logging;

public class Log
{
  private static Log? _instance;
  public static Log Instance => _instance ??= new Log();

  [Flags]
  public enum Place : uint
  {
    None = 0, // disable logging
    Console = 1 << 0,
  }

  [Flags]
  public enum Kind
  {
    [LogMarker("*")]
    [LogConsoleColor(ConsoleColor.Black, ConsoleColor.White)]
    Info = 1 << 0,

    [LogMarker("!")]
    [LogConsoleColor(ConsoleColor.DarkYellow, ConsoleColor.Black)]
    Warning = 1 << 1,

    [LogMarker("X")]
    [LogConsoleColor(ConsoleColor.DarkRed, ConsoleColor.White)]
    Error = 1 << 2,
    
    [LogMarker("~")]
    [LogConsoleColor(ConsoleColor.DarkGray, ConsoleColor.White)]
    Debug = 1 << 3,
    
    [LogMarker("o")]
    [LogConsoleColor(ConsoleColor.DarkMagenta, ConsoleColor.White)]
    Success = 1 << 4,

  }

  readonly object writeLock = new();

  Place writingPlaces = Place.Console;
  Kind allowedKinds =
    Kind.Info | Kind.Warning | Kind.Error | Kind.Success;

  int spaces = 0;
  readonly Stack<int> savedSpaces = new();

  bool coloredConsole;

  public void TurnOff()
  {
    lock (writeLock)
    {
      writingPlaces = Place.None;
    }
  }

  public void AllowWritingPlace(Place place, bool allow)
  {
    lock (writeLock)
    {
      if (allow)
      {
        writingPlaces |= place;
      }
      else
      {
        writingPlaces &= ~place;
      }
    }
  }

  public void AllowKind(Kind kind, bool allow)
  {
    lock (writeLock)
    {
      if (allow)
      {
        allowedKinds |= kind;
      }
      else
      {
        allowedKinds &= ~kind;
      }
    }
  }

  public void SetColoredConsole(bool isColored)
  {
    lock (writeLock)
    {
      coloredConsole = isColored;
    }
  }

  public int StartSteps(string? header = null)
  {
    lock (writeLock)
    {
      if (!string.IsNullOrEmpty(header))
      {
        Write(Kind.Info, @$"*** {header} ***");
      }
      savedSpaces.Push(spaces);
      spaces += 2;
      return savedSpaces.Count;
    }
  }

  public void EndSteps(int level = -1, string? footer = null)
  {
    lock (writeLock)
    {
      if (savedSpaces.Count > 0)
      {
        if (level < 0)
        {
          spaces = savedSpaces.Pop();
        }
        else
        {
          while (savedSpaces.Count >= level && savedSpaces.Count > 0)
          {
            spaces = savedSpaces.Pop();
          }
        }
      }


      if (!string.IsNullOrEmpty(footer))
      {
        Write(Kind.Info, $"___ {footer} ___\n");
      }
    }
  }

  public void Write(Kind kind, object? obj)
  {
    lock (writeLock)
    {
      if (!allowedKinds.HasFlag(kind))
      {
        return;
      }
      
      var msg = FormatMessage(kind, obj);

      if (writingPlaces.HasFlag(Place.Console))
      {
        WriteConsole(kind, msg);
      }

    }
  }

  public void WriteException(Exception e)
  {
    Write(Kind.Error, FormatException(e));
  }

  string FormatException(Exception? e)
  {
    if (e is null)
    {
      return string.Empty;
    }

    string err = "$ " + e.Message + "\n"
      + $"XXXXXXXXXXXXXXXXXXXXXXXX  VVVVVV\n"
      + e.ToString() + "\n"
      + $"XXXXXXXXXXXXXXXXXXXXXXXX  ^^^^^^";

    var innerException = FormatException(e.InnerException);

    return string.IsNullOrEmpty(innerException) ? err : err + "\n" + innerException;
  }


  (string TimeFormatted, string Spaces, string LogKindFormatted, string ObjString) FormatMessage(Kind kind, object? obj)
  {
    return (
       $"{DateTime.Now:yyyy/MM/dd - HH:mm:ss.fff}",
       $"{new string(' ', spaces)}",
       $"[{kind.GetEnumAttribute<LogMarkerAttribute, Kind>()?.Marker}]",
       $"{obj}"
    );
  }

  void WriteConsole(Kind kind, (string TimeFormatted, string Spaces, string LogKindFormatted, string ObjString) msg)
  {
    var backupBg = Console.BackgroundColor;
    var backupFg = Console.ForegroundColor;

    if (coloredConsole)
    {
      Console.BackgroundColor = ConsoleColor.Black;
      Console.ForegroundColor = ConsoleColor.White;
    }
    Console.Write($"({msg.TimeFormatted}) {msg.Spaces}");

    if (coloredConsole)
    {
      var colorAtt = kind.GetEnumAttribute<LogConsoleColorAttribute, Kind>();
      if (colorAtt is null)
      {
        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.White;
      }
      else
      {
        Console.BackgroundColor = colorAtt.Background;
        Console.ForegroundColor = colorAtt.Foreground;
      }
    }

    Console.WriteLine($"{msg.LogKindFormatted} {msg.ObjString}");

    if (coloredConsole)
    {
      Console.BackgroundColor = backupBg;
      Console.ForegroundColor = backupFg;
    }
  }

}
