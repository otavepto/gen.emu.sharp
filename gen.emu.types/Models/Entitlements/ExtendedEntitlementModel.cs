using System.Text.Json.Nodes;

namespace gen.emu.types.Models.Entitlements;

public class ExtendedEntitlementModel : EntitlementModel
{
  // from steam storefront api: https://wiki.teamfortress.com/wiki/User:RJackson/StorefrontAPI#appdetails
  public JsonObject AppDetails { get; set; } = new();

}
