using gen.emu.types.Models.Branches;
using gen.emu.types.Models.Entitlements;
using gen.emu.types.Models.Inventory;
using gen.emu.types.Models.MediaAssets;
using gen.emu.types.Models.StatsAndAchievements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace gen.emu.types.Models;

public class AppInfoModel
{
  public uint AppId { get; set; }

  [JsonInclude]
  public ExtendedEntitlementModel Product { get; private set; } = new();

  [JsonInclude]
  public IReadOnlyList<string> SupportedLanguages { get; private set; } = new List<string>();

  public JsonObject LaunchConfigurations { get; set; } = new();

  [JsonInclude]
  public IReadOnlyDictionary<uint, ExtendedEntitlementModel> Demos { get; private set; } = new Dictionary<uint, ExtendedEntitlementModel>();

  [JsonInclude]
  public AppDepotsModel Depots { get; private set; } = new();

  [JsonInclude]
  public BranchesModel Branches { get; private set; } = new();

  [JsonInclude]
  public IReadOnlyDictionary<uint, ExtendedEntitlementModel> Dlcs { get; private set; } = new Dictionary<uint, ExtendedEntitlementModel>();

  // IDs of top reviewers for this app on the store
  [JsonInclude]
  public IReadOnlyList<ulong> TopReviewers { get; private set; } = new List<ulong>();

  [JsonInclude]
  public StatsAndAchievementsModel StatsAndAchievements { get; private set; } = new();

  [JsonInclude]
  public IReadOnlyList<ControllerInfoModel > ControllerInfo { get; private set; } = new List<ControllerInfoModel>();

  [JsonInclude]
  public InventoryModel InventoryItems { get; private set; } = new();

  [JsonInclude]
  public MediaAssetsModel MediaAssets { get; private set; } = new();

}
