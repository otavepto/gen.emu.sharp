using gen.emu.types.Models.MediaAssets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models.StatsAndAchievements;

public class AchievementModel
{
  public int Id { get; set; } = -1;

  public string InternalName { get; set; } = string.Empty;

  // appid 480 has a mix of objects (translated strings) and simple strings (english only)
  [JsonInclude]
  public JsonNode FriendlyNameTranslations { get; set; } = string.Empty;

  // appid 480 has a mix of objects (translated strings) and simple strings (english only)
  [JsonInclude]
  public JsonNode DescriptionTranslations { get; set; } = string.Empty;

  public bool IsHidden { get; set; } = false;

  [JsonInclude]
  public MediaAssetItemModel IconUnlocked { get; private set; } = new();

  [JsonInclude]
  public MediaAssetItemModel IconLocked { get; private set; } = new();

  public JsonObject? ProgressDetails { get; set; } = null;

}
