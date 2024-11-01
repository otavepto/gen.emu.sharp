using gen.emu.types.Models.MediaAssets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models.Inventory;

public class InventoryIconsModel
{
  [JsonInclude]
  public MediaAssetItemModel Icon { get; private set; } = new();

  [JsonInclude]
  public MediaAssetItemModel IconLarge { get; private set; } = new();

}
