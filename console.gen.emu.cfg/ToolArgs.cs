using CommandLine;
using CommandLine.Text;
using System.Globalization;
using System.Text;

namespace console.gen.emu.cfg;

public class ToolArgs
{
  private static ToolArgs? _instance;
  public static ToolArgs Instance => _instance ??= new ToolArgs();


  // https://github.com/commandlineparser/commandline
  public class Options
  {
    [Option('u', "uname", Default = null, Required = false,
      HelpText = "login unsername")]
    public string? Username { get; private set; } = null;

    [Option('p', "upass", Default = null, Required = false,
      HelpText = "login password")]
    public string? Password { get; private set; } = null;

    [Option("anon", Default = false, Required = false,
      HelpText = "login as an anonymous account, these have very limited access and cannot get all app details")]
    public bool AnonLogin { get; private set; }

    [Option("offline", Default = false, Required = false,
      HelpText = "don't make any online connections and restore app details from backup")]
    public bool IsOfflineMode { get; private set; }



    [Option("frombackup", Default = false, Required = false,
      HelpText = "restore each app information from previous backup")]
    public bool RestoreFromBackup { get; private set; }

    [Option("reldir", Default = false, Required = false,
      HelpText = "use current working directory to the current working directory")]
    public bool UseRelativeOutputDir { get; private set; }
    


    [Option("noextrainfo", Default = false, Required = false,
      HelpText = "don't query the Steam API (appdetails) for extra app info")]
    public bool SkipAdditionalInfo { get; private set; }

    [Option("noinv", Default = false, Required = false,
      HelpText = "skip downloading inventory items definitions")]
    public bool SkipInventoryItems { get; private set; }

    [Option("nodemos", Default = false, Required = false,
      HelpText = "skip downloading info about any demos available for the app")]
    public bool SkipDemos { get; private set; }

    [Option("nodlcs", Default = false, Required = false,
      HelpText = "skip downloading info about any DLCs available for the app")]
    public bool SkipDlcs { get; private set; }

    [Option("nocontroller", Default = false, Required = false,
      HelpText = "skip downloading controller info & configuration files")]
    public bool SkipController { get; private set; }

    [Option("noreview", Default = false, Required = false,
      HelpText = "don't query Steam API for the IDs of the app reviewers")]
    public bool SkipReviewers { get; private set; }

    [Option("nostatsach", Default = false, Required = false,
      HelpText = "skip downloading stats & achievements schema")]
    public bool SkipStatsAndAchievements { get; private set; }

    [Option("noachicon", Default = false, Required = false,
      HelpText = "skip downloading achievements icons")]
    public bool SkipAchievementsIcons { get; private set; }



    [Option("icons", Default = false, Required = false,
      HelpText = "download some icons for each app")]
    public bool DownloadIcons { get; private set; }

    [Option("imgs", Default = false, Required = false,
      HelpText = "download common images for each app: steam generated background, icon, logo, etc...")]
    public bool DownloadCommonImages { get; private set; }

    [Option("scrn", Default = false, Required = false,
      HelpText = "download screenshots for each app if they're available")]
    public bool DownloadScreenshots { get; private set; }

    [Option("thumbs", Default = false, Required = false,
      HelpText = "download screenshots thumbnails for each app if they're available")]
    public bool DownloadScreenshotsThumbnails { get; private set; }

    [Option("vid", Default = false, Required = false,
      HelpText = "download the first video available for each app: trailer, gameplay, announcement, etc...")]
    public bool DownloadVideo { get; private set; }
    
    [Option("invicons", Default = false, Required = false,
      HelpText = "download small inventory icons (WARNING takes long time)")]
    public bool DownloadInventoryIcons { get; private set; }
    
    [Option("inviconslarge", Default = false, Required = false,
      HelpText = "download large inventory icons (WARNING takes long time)")]
    public bool DownloadInventoryLargeIcons { get; private set; }



    //[Option("template", Default = "", Required = false,
    //  HelpText = "path to a file to use as a template, its content will be substituted with the actual app details")]
    //public string TemplateFilepath { get; private set; } = string.Empty;



    [Option("nolog", Default = false, Required = false,
      HelpText = "disable logging")]
    public bool DisableLogging { get; private set; }

    [Option('v', "verbose", Default = false, Required = false,
      HelpText = "make logging verbose")]
    public bool VerboseLogging { get; private set; } = false;

    [Option("nologcolor", Default = false, Required = false,
      HelpText = "don't output colored logs in the console")]
    public bool NoColoredConsoleLog { get; private set; }



    [Usage]
    public static IEnumerable<Example> Examples
    {
      get
      {
        yield return new Example("Example: grab everything", new Options
        {
          VerboseLogging = true,
          DownloadIcons = true,
          DownloadCommonImages = true,
          DownloadScreenshots = true,
          DownloadScreenshotsThumbnails = true,
          DownloadVideo = true,
          DownloadInventoryIcons = true,
          DownloadInventoryLargeIcons = true,
        });
      }
    }
  }


  Options? options;
  public Options GetOptions => options is not null ? options : throw new InvalidOperationException("Not parsed yet");

  readonly HashSet<uint> appids = [];
  public IReadOnlyList<uint> GetAppIds => appids.ToList();

  public string HelpText { get; private set; } = string.Empty;

  public string VersionText { get; private set; } = string.Empty;

  public void ParseCmdline(IEnumerable<string> args)
  {
    options = null;
    appids.Clear();
    List<string> strings = [];
    foreach (string arg in args)
    {
      if (uint.TryParse(arg, CultureInfo.InvariantCulture, out var appid))
      {
        appids.Add(appid);
      }
      else
      {
        strings.Add(arg);
      }
    }

    using var helpOrVerWriter = new StringWriter();

    void OnError(IEnumerable<Error> errors)
    {
      foreach (var error in errors)
      {
        switch (error.Tag)
        {
          case ErrorType.UnknownOptionError:
            {
              var err = (UnknownOptionError)error;
              if (err.Token.Equals("help", StringComparison.OrdinalIgnoreCase))
              {
                HelpText = helpOrVerWriter.ToString();
              }
              else if (err.Token.Equals("version", StringComparison.OrdinalIgnoreCase))
              {
                VersionText = helpOrVerWriter.ToString();
              }
              else
              {
                throw new ArgumentException($"Invalid argument <Type: {error.Tag} - {error.GetType()}>: '{error}'");
              }
            }
            break;
          case ErrorType.HelpRequestedError:
          case ErrorType.HelpVerbRequestedError:
            HelpText = helpOrVerWriter.ToString();
            break;
          case ErrorType.VersionRequestedError:
            VersionText = helpOrVerWriter.ToString();
            break;
          default:
            throw new ArgumentException($"Invalid argument <Type: {error.Tag} - {error.GetType()}>: '{error}'");
        }
      }
    }

    var parser = new Parser(settings =>
    {
      settings.IgnoreUnknownArguments = true;
      settings.ParsingCulture = CultureInfo.InvariantCulture;
      settings.AutoHelp = true;
      settings.HelpWriter = helpOrVerWriter;
      settings.AutoVersion = true;
    });
    parser
      .ParseArguments<Options>(strings)
      .WithParsed(opt => options = opt)
      .WithNotParsed(OnError)
      ;
  }

}
