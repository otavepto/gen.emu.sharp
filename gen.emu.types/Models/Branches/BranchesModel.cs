using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models.Branches;

public class BranchesModel
{
  public JsonObject OriginalSchema { get; set; } = new();

  [JsonInclude]
  public IReadOnlyList<BranchItemModel> Branches { get; private set; } = new List<BranchItemModel>();

}
