using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using gen.emu.types.Models;
using gen.emu.types.Generators;
using common.utils.Logging;

namespace gen.emu.cfg;

public class GeneratorsRunner
{
  private static GeneratorsRunner? _instance;
  public static GeneratorsRunner Instance => _instance ??= new GeneratorsRunner();


  public async Task RunForAppAsync(AppInfoModel app, string basepath, IEnumerable<string> extraArgs, IEnumerable<IGenerator> generators)
  {
    ArgumentNullException.ThrowIfNull(app);
    ArgumentNullException.ThrowIfNull(generators);

    foreach (var gen in generators)
    {
      var lglvgen = Log.Instance.StartSteps($"Running generator <{gen.GetType().Name}>");

      var lglvargs = Log.Instance.StartSteps($"Parsing args");
      await RunParseArgs(gen, extraArgs).ConfigureAwait(false);
      Log.Instance.EndSteps(lglvargs);

      var lglvsetup = Log.Instance.StartSteps($"Setting up");
      bool setupSucceeded = await RunSetupAsync(gen, basepath).ConfigureAwait(false);
      Log.Instance.EndSteps(lglvsetup);
      if (setupSucceeded)
      {
        if (!setupSucceeded)
        {
          Log.Instance.Write(Log.Kind.Warning, $"Skipping this generator");
          break;
        }

        var lglvapp = Log.Instance.StartSteps($"Generating for appid {app.AppId}");
        await RunOnAppAsync(gen, app).ConfigureAwait(false);
        Log.Instance.EndSteps(lglvapp);

        var lglvclean = Log.Instance.StartSteps($"Cleaning up");
        bool cleanupSucceeded = await RunCleanupAsync(gen).ConfigureAwait(false);
        Log.Instance.EndSteps(lglvclean);
        if (!cleanupSucceeded)
        {
          Log.Instance.Write(Log.Kind.Warning, $"Skipping this generator");
          break;
        }
      }
      else
      {
        Log.Instance.Write(Log.Kind.Warning, $"Skipping this generator");
      }

      Log.Instance.EndSteps(lglvgen);
    }

  }


  async Task RunParseArgs(IGenerator gen, IEnumerable<string> extraArgs)
  {
    try
    {
      await gen.ParseArgs(extraArgs).ConfigureAwait(false);
      Log.Instance.Write(Log.Kind.Success, $"done");
    }
    catch (Exception ex)
    {
      Log.Instance.WriteException(ex);
      Log.Instance.Write(Log.Kind.Error, $"failed");
    }
  }

  async Task<bool> RunSetupAsync(IGenerator gen, string basepath)
  {
    try
    {
      await gen.Setup(basepath).ConfigureAwait(false);
      Log.Instance.Write(Log.Kind.Success, $"done");
      return true;
    }
    catch (Exception ex)
    {
      Log.Instance.WriteException(ex);
      Log.Instance.Write(Log.Kind.Error, $"failed");
    }
    
    return false;

  }

  async Task RunOnAppAsync(IGenerator gen, AppInfoModel appInfoModel)
  {
    try
    {
      await gen.Generate(appInfoModel).ConfigureAwait(false);
      Log.Instance.Write(Log.Kind.Success, $"done");
    }
    catch (Exception e)
    {
      Log.Instance.WriteException(e);
      Log.Instance.Write(Log.Kind.Error, $"failed");
    }
  }

  async Task<bool> RunCleanupAsync(IGenerator gen)
  {
    try
    {
      await gen.Cleanup().ConfigureAwait(false);
      Log.Instance.Write(Log.Kind.Success, $"done");
      return true;
    }
    catch (Exception ex)
    {
      Log.Instance.WriteException(ex);
      Log.Instance.Write(Log.Kind.Error, $"failed");
    }
    return false;
  }

}
