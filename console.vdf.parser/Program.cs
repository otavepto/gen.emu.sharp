using common.utils;
using common.utils.Logging;
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
  Log.Instance.Write(Log.Kind.Info, $"\nUsage: {exePath} path/to/file_1.vdf path/to/file_2.vdf ...\n");
  throw new ArgumentException($"At least one valid filepath must be provided");
}

Log.Instance.Write(Log.Kind.Info, $"Parsing [{filepaths.Count}] files");

foreach (var item in filepaths)
{
  var lglvfile = Log.Instance.StartSteps($"Parsing VDF file '{item}'");
  try
  {
    var vdfObj = GetVdfFileData(item);
    var presets_actions_bindings = GseGenerator.ParseControllerVdfObj(vdfObj);

    if (presets_actions_bindings.Count > 0)
    {
      var baseFolder = Path.Combine("generated", Path.GetFileNameWithoutExtension(item));
      var controllerFolder = Path.Combine(baseFolder, "controller");
      var backupFolder = Path.Combine(baseFolder, "backup");
      Directory.CreateDirectory(controllerFolder);
      Directory.CreateDirectory(backupFolder);
      Utils.WriteJson(vdfObj, Path.Combine(backupFolder, "converted.json"));
      foreach (var (presetName, presetObj) in presets_actions_bindings)
      {
        List<string> filecontent = [];
        foreach (var (actionName, actionBindingsSet) in presetObj)
        {
          filecontent.Add($"{actionName}={string.Join(',', actionBindingsSet)}");
        }

        var filepath = Path.Combine(controllerFolder, $"{presetName}.txt");
        File.WriteAllLines(filepath, filecontent, Utils.Utf8EncodingNoBom);
      }

      Log.Instance.Write(Log.Kind.Success, $"Written [{presets_actions_bindings.Count}] presets");
    }
    else
    {
      Log.Instance.Write(Log.Kind.Warning, $"No supported presets were found");
    }

  }
  catch (Exception ex)
  {
    Log.Instance.WriteException(ex);
    Log.Instance.Write(Log.Kind.Error, $"Failed: {ex.Message}");
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
  return Helpers.CreateVdfObj(vdfStream);
}