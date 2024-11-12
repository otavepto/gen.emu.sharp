using common.utils;
using common.utils.Logging;
using gen.emu.cfg;
using gen.emu.shared;
using generator.gse;
using System.Text.Json.Nodes;


Log.Instance.AllowKind(Log.Kind.Debug, true);
Log.Instance.SetColoredConsole(true);

List<string> filepaths = [];
foreach (var item in args)
{
  if (File.Exists(item))
  {
    filepaths.Add(item);
  }
  else
  {
    Log.Instance.Write(Log.Kind.Warning, $"File '{item}' is invalid (doesn't exist or no access), ignoring it");
  }
}

if (filepaths.Count == 0)
{
  var exePath = Path.GetFileNameWithoutExtension(typeof(Program).Assembly.Location);
  Log.Instance.Write(Log.Kind.Info, $"\nUsage: {exePath} path/to/UserGameStatsSchema_480.bin path/to/UserGameStatsSchema_730.bin ...\n");
  throw new ArgumentException($"At least one valid filepath must be provided");
}

Log.Instance.Write(Log.Kind.Info, $"Parsing [{filepaths.Count}] files");
var gseGen = new GseGenerator();

foreach (var item in filepaths)
{
  var lglvfile = Log.Instance.StartSteps($"Parsing VDF file '{item}'");
  try
  {
    var baseFolder = Path.Combine(Utils.GetExeDir(), "generated", Path.GetFileNameWithoutExtension(item));
    var backupFolder = Path.Combine(baseFolder, "backup");

    var vdfObj = GetVdfFileData(item);

    Directory.CreateDirectory(backupFolder);
    Utils.WriteJson(vdfObj, Path.Combine(backupFolder, "converted.json"));

    var (stats, achs) = AppStats.Instance.ParseStatsSchema(vdfObj);

    await gseGen.Setup(baseFolder).ConfigureAwait(false);
    gseGen.SaveStats(stats);
    await gseGen.SaveAchievements(achs.AsReadOnly());

    Log.Instance.Write(Log.Kind.Success, $"Done");
  }
  catch (Exception ex)
  {
    Log.Instance.WriteException(ex);
    Log.Instance.Write(Log.Kind.Error, $"Failed: {ex.Message}");
  }

  try
  {
    await gseGen.Cleanup();
  }
  catch (Exception ex)
  {
    Log.Instance.WriteException(ex);
  }

  Log.Instance.EndSteps(lglvfile);
}

JsonObject GetVdfFileData(string filepath)
{
  using var vdfStream = new FileStream(filepath, new FileStreamOptions
  {
    Access = FileAccess.Read,
    Mode = FileMode.Open,
    Share = FileShare.Read,
  });
  return Helpers.LoadVdf(vdfStream, Helpers.VdfType.Binary);
}