using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models;

public class ControllerInfoModel
{
  public ulong Id { get; set; }

  public string Filename { get; set; } = string.Empty;

  public string ControllerType { get; set; } = string.Empty;

  [JsonInclude]
  public IReadOnlyList<string> EnabledBranches { get; private set; } =
    new List<string>();

  public bool UseActionBlock { get; set; }

  public JsonObject VdfData { get; set; } = new();

}
