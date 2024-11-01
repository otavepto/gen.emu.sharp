using System.Text.Json.Nodes;

namespace gen.emu.types.Models.Entitlements;

public class EntitlementModel
{
  // original name
  public string NameInStore { get; set; } = string.Empty;

  // safe name to save on disk
  public string NameOnDisk { get; set; } = string.Empty;

  // from SteamApps api
  public JsonObject ProductInfo { get; set; } = new();

}
