using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models.UserFileSystem;

public class SaveFileOverrideModel
{
  // which original save file is this override targetting
  // any save file whose root matches this prop value should be considered
  // otherwise, the original save file is left without changes
  public string RootOriginal { get; set; } = string.Empty;

  // the new value for the root dir
  public string RootNew { get; set; } = string.Empty;

  // which platform is this overrride meant for
  public string Platform { get; set; } = string.Empty;

  // a direct/literal string to append blindly after the new root
  public string PathAfterRootNew { get; set; } = string.Empty;

  // the patterns in the original save file to replace
  [JsonInclude]
  public IReadOnlyList<SavePathTransformModel> PathsToTransform { get; private set; } =
    new List<SavePathTransformModel>();

}
