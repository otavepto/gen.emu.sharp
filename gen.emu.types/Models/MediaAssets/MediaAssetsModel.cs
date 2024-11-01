using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;


namespace gen.emu.types.Models.MediaAssets;

public class MediaAssetsModel
{
  [JsonInclude]
  public IReadOnlyList<MediaAssetItemModel> Icons { get; private set; } = [
    new MediaAssetItemModel() { Name = "clienticon|ico", NameOnDisk = "clienticon.ico", },
    new MediaAssetItemModel() { Name = "icon|jpg", NameOnDisk = "icon.jpg", },
    new MediaAssetItemModel() { Name = "logo|jpg", NameOnDisk = "logo.jpg", },
    new MediaAssetItemModel() { Name = "logo_small|jpg", NameOnDisk = "logo_small.jpg", },
  ];

  [JsonInclude]
  public IReadOnlyList<MediaAssetItemModel> CommonImages { get; private set; } = [
    new MediaAssetItemModel() { Name = "capsule_184x69.jpg", NameOnDisk = "capsule_184x69.jpg" },
    new MediaAssetItemModel() { Name = "capsule_231x87.jpg", NameOnDisk = "capsule_231x87.jpg" },
    new MediaAssetItemModel() { Name = "capsule_231x87_alt_assets_0.jpg", NameOnDisk = "capsule_231x87_alt_assets_0.jpg" },
    new MediaAssetItemModel() { Name = "capsule_467x181.jpg", NameOnDisk = "capsule_467x181.jpg" },
    new MediaAssetItemModel() { Name = "capsule_616x353.jpg", NameOnDisk = "capsule_616x353.jpg" },
    new MediaAssetItemModel() { Name = "capsule_616x353_alt_assets_0.jpg", NameOnDisk = "capsule_616x353_alt_assets_0.jpg" },

    new MediaAssetItemModel() { Name = "hero_capsule.jpg", NameOnDisk = "hero_capsule.jpg" },

    new MediaAssetItemModel() { Name = "library_600x900.jpg", NameOnDisk = "library_600x900.jpg" },
    new MediaAssetItemModel() { Name = "library_600x900_2x.jpg", NameOnDisk = "library_600x900_2x.jpg" },

    new MediaAssetItemModel() { Name = "library_hero.jpg", NameOnDisk = "library_hero.jpg" },
    new MediaAssetItemModel() { Name = "library_hero_2x.jpg", NameOnDisk = "library_hero_2x.jpg" },

    new MediaAssetItemModel() { Name = "broadcast_left_panel.jpg", NameOnDisk = "broadcast_left_panel.jpg" },
    new MediaAssetItemModel() { Name = "broadcast_right_panel.jpg", NameOnDisk = "broadcast_right_panel.jpg" },

    new MediaAssetItemModel() { Name = "page.bg.jpg", NameOnDisk = "page.bg.jpg" },
    new MediaAssetItemModel() { Name = "page_bg_raw.jpg", NameOnDisk = "page_bg_raw.jpg" },
    new MediaAssetItemModel() { Name = "page_bg_generated.jpg", NameOnDisk = "page_bg_generated.jpg" },
    new MediaAssetItemModel() { Name = "page_bg_generated_v6b.jpg", NameOnDisk = "page_bg_generated_v6b.jpg" },

    new MediaAssetItemModel() { Name = "header.jpg", NameOnDisk = "header.jpg" },
    new MediaAssetItemModel() { Name = "header_alt_assets_0.jpg", NameOnDisk = "header_alt_assets_0.jpg" },

    new MediaAssetItemModel() { Name = "logo.png", NameOnDisk = "logo.png" },
    new MediaAssetItemModel() { Name = "logo_2x.png", NameOnDisk = "logo_2x.png" },
  ];
  
  [JsonInclude]
  public IReadOnlyList<MediaAssetItemModel> Screenshots { get; private set; } = new List<MediaAssetItemModel>();

  [JsonInclude]
  public IReadOnlyList<MediaAssetItemModel> ScreenshotsThumbnails { get; private set; } = new List<MediaAssetItemModel>();

  // Promo, trailer, showcase, etc...
  public MediaAssetItemModel? Video { get; set; }

}
