using CommandLine;
using common.utils;
using common.utils.Logging;
using gen.emu.shared;
using gen.emu.types.Generators;
using gen.emu.types.Models;
using gen.emu.types.Models.StatsAndAchievements;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace generator.gse;


public class GseGenerator : IGenerator
{
  // https://github.com/commandlineparser/commandline
  public class Options
  {
    [Option("nonet", Default = false, Required = false,
      HelpText = "disable networking (this won't prevent games from making external web requests)")]
    public bool DisableNetworking { get; private set; }

  }

  const string ACHIEVEMENT_IMAGE_FOLDER_NAME = "ach_images";
  const string ACHIEVEMENT_IMAGE_LOCKED_FOLDER_NAME = "locked";

  const string DEFAULT_ACH_ICON_RSRC_UNLOCKED = "ach_unlocked_default.jpg";
  const string DEFAULT_ACH_ICON_RSRC_LOCKED = "ach_locked_default.jpg";

  AppInfoModel appInfoModel = default!;

  string baseFolder = string.Empty;

  string extraInfoFolder = string.Empty;

  string settingsFolder = string.Empty;
  string controllerFolder = string.Empty;
  string modsFolder = string.Empty;
  string modsImagesFolder = string.Empty;
  string achievementsImagesFolder = string.Empty;
  string achievementsImagesLockedFolder = string.Empty;

  List<string> supportedLangs = [];

  readonly IniManager iniFiles = new();

  Options options = new();

  public string GenerateVersion()
  {
    using var stwr = new StringWriter();

    var parser = new Parser(settings => settings.HelpWriter = stwr);
    parser.ParseArguments<Options>(["--version"]);

    return stwr.ToString();
  }

  public string GenerateHelpPage()
  {
    using var stwr = new StringWriter();

    var parser = new Parser(settings => settings.HelpWriter = stwr);
    parser.ParseArguments<Options>(["--help"]);

    return stwr.ToString();
  }

  public Task ParseArgs(IEnumerable<string> args)
  {
    var parser = new Parser(settings =>
    {
      settings.IgnoreUnknownArguments = true;
      settings.ParsingCulture = CultureInfo.InvariantCulture;
    });
    parser
      .ParseArguments<Options>(args)
      .WithParsed(opt => options = opt)
      ;
    // TODO
    return Task.CompletedTask;
    throw new NotImplementedException();
  }

  public Task Setup(string basepath)
  {
    baseFolder = Path.Combine(basepath, "gse");
    return Task.CompletedTask;
  }

  public async Task Generate(AppInfoModel appInfoModel)
  {
    this.appInfoModel = appInfoModel;

    extraInfoFolder = Path.Combine(baseFolder, "extra_info");

    settingsFolder = Path.Combine(baseFolder, "steam_settings");
    controllerFolder = Path.Combine(settingsFolder, "controller");
    modsFolder = Path.Combine(settingsFolder, "mods");
    modsImagesFolder = Path.Combine(settingsFolder, "mod_images");
    achievementsImagesFolder = Path.Combine(settingsFolder, ACHIEVEMENT_IMAGE_FOLDER_NAME);
    achievementsImagesLockedFolder = Path.Combine(achievementsImagesFolder, ACHIEVEMENT_IMAGE_LOCKED_FOLDER_NAME);

    Directory.CreateDirectory(baseFolder);

    SaveAppid();
    var invTask = SaveAchievements();
    SaveInventory();
    SaveExtraInfo();
    SaveDepots();
    SaveSupportedLanguages();
    SaveBranches();
    SaveDlcs();
    SaveStats();
    SaveController();
    DisableExtraFeatures();

    await invTask.ConfigureAwait(false);
    
  }

  private void DisableExtraFeatures()
  {
    if (options.DisableNetworking)
    {
      var mainConn = iniFiles.GetSection("configs.main.ini", "main::connectivity");
      mainConn["disable_networking"] = ("1", "disable all steam networking interface functionality");
      mainConn["disable_sharing_stats_with_gameserver"] = ("1", "prevent sharing stats and achievements with any game server, this also disables the interface ISteamGameServerStats");
      mainConn["disable_source_query"] = ("1", "do not send server details to the server browser, only works for game servers");
    }

  }

  public Task Cleanup()
  {
    Directory.CreateDirectory(settingsFolder);
    iniFiles.WriteAllFiles(settingsFolder);

    appInfoModel = null!;

    extraInfoFolder = string.Empty;

    settingsFolder = string.Empty;
    controllerFolder = string.Empty;
    modsFolder = string.Empty;
    modsImagesFolder = string.Empty;
    achievementsImagesFolder = string.Empty;
    achievementsImagesLockedFolder = string.Empty;

    supportedLangs.Clear();

    iniFiles.Clear();

    return Task.CompletedTask;
  }


  void SaveAppid()
  {
    Directory.CreateDirectory(settingsFolder);
    File.WriteAllText(Path.Combine(settingsFolder, "steam_appid.txt"),
      appInfoModel.AppId.ToString(CultureInfo.InvariantCulture),
    Utils.Utf8EncodingNoBom);
  }

  void SaveExtraInfo()
  {
    bool gotAny =
      appInfoModel.LaunchConfigurations.Count > 0
      || appInfoModel.Product.ProductInfo.Count > 0
      || appInfoModel.Product.AppDetails.Count > 0;
    if (!gotAny)
    {
      return;
    }

    Directory.CreateDirectory(extraInfoFolder);

    static void WriteObj(JsonObject obj, string filepath)
    {
      if (obj.Count == 0)
      {
        return;
      }

      Utils.WriteJson(obj, filepath);
    }

    WriteObj(appInfoModel.LaunchConfigurations, Path.Combine(extraInfoFolder, "launch_config.json"));
    WriteObj(appInfoModel.Product.ProductInfo, Path.Combine(extraInfoFolder, "product_info.json"));
    WriteObj(appInfoModel.Product.AppDetails, Path.Combine(extraInfoFolder, "app_details.json"));
  }

  void SaveStats()
  {
    var stats = appInfoModel.StatsAndAchievements.Stats
      .Where(s => s.InternalName.Length > 0)
      .ToList();
    stats.Sort((a, b) => a.Id.CompareTo(b.Id));

    if (stats.Count == 0)
    {
      return;
    }

    Directory.CreateDirectory(settingsFolder);
    File.WriteAllLines(Path.Combine(settingsFolder, "stats.txt"), stats.Select(s =>
      $"{s.InternalName}={s.Type.GetEnumAttribute<EnumMemberAttribute, StatType>()?.Value}={(int)s.DefaultValue}"
    ), Utils.Utf8EncodingNoBom);
    
  }

  void SaveDepots()
  {
    var depots = appInfoModel.Depots.Depots
      .Where(d => d != 0);

    if (!depots.Any())
    {
      return;
    }

    Directory.CreateDirectory(settingsFolder);
    File.WriteAllLines(Path.Combine(settingsFolder, "depots.txt"), depots.Select(d =>
      d.ToString(CultureInfo.InvariantCulture)
    ), Utils.Utf8EncodingNoBom);

  }

  void SaveSupportedLanguages()
  {
    supportedLangs = [.. appInfoModel.SupportedLanguages];
    if (supportedLangs.Count == 0)
    {
      Log.Instance.Write(Log.Kind.Debug, $"no supported languages found, forcing english as a supported language");
      supportedLangs.Add("english");
    }

    Directory.CreateDirectory(settingsFolder);
    File.WriteAllLines(Path.Combine(settingsFolder, "supported_languages.txt"), supportedLangs, Utils.Utf8EncodingNoBom);
  }

  void SaveBranches()
  {
    var branches = appInfoModel.Branches.Branches
      .Select(br => new JsonObject
        {
          ["name"] = br.Name,
          ["description"] = br.Description,
          ["protected"] = br.IsProtected,
          ["build_id"] = br.BuildId,
          ["time_updated"] = br.TimeUpdated,
        }).ToList();
    if (branches.Count == 0)
    {
      Log.Instance.Write(Log.Kind.Debug, $"no branches found, adding 'public' as a branch");
      branches.Add(new JsonObject
      {
        ["name"] = "public",
        ["description"] = "",
        ["protected"] = false,
        ["build_id"] = 10,
        ["time_updated"] = Utils.GetUnixEpoch(),
      });
    }

    Directory.CreateDirectory(settingsFolder);
    Utils.WriteJson(branches, Path.Combine(settingsFolder, "branches.json"));
  }

  void SaveDlcs()
  {
    var dlcs = appInfoModel.Dlcs
      .Where(d => d.Key != 0)
      .Select(d =>
        new KeyValuePair<string, (string, string?)>(
          d.Key.ToString(CultureInfo.InvariantCulture), (d.Value.NameInStore, null)
        )
      );

    var dlcSection = iniFiles.GetSection("configs.app.ini", "app::dlcs");
    dlcSection["unlock_all"] = ("0", "should the emu report all DLCs as unlocked");
    dlcSection.CopyFrom(dlcs);

  }

  readonly static HashSet<string> supported_controllers_types = [
    "controller_xbox360",
    "controller_xboxone",
    "controller_steamcontroller_gordon",

    // TODO not sure about these
    "controller_ps5",
    "controller_ps4",
    "controller_switch_pro",
    "controller_neptune",
  ];

  readonly static Dictionary<string, string> keymap_digital = new()
  {
    { "button_a", "A" },
    { "button_b", "B" },
    { "button_x", "X" },
    { "button_y", "Y" },
    { "dpad_north", "DUP" },
    { "dpad_south", "DDOWN" },
    { "dpad_east", "DRIGHT" },
    { "dpad_west", "DLEFT" },
    { "button_escape", "START" },
    { "button_menu", "BACK" },
    { "left_bumper", "LBUMPER" },
    { "right_bumper", "RBUMPER" },
    { "button_back_left", "Y" },
    { "button_back_right", "A" },
    { "button_back_left_upper", "X" },
    { "button_back_right_upper", "B" },
  };
  readonly static Dictionary<string, string> keymap_left_joystick = new()
  {
    { "dpad_north", "DLJOYUP" },
    { "dpad_south", "DLJOYDOWN" },
    { "dpad_west", "DLJOYLEFT" },
    { "dpad_east", "DLJOYRIGHT" },
    { "click", "LSTICK" },
  };
  readonly static Dictionary<string, string> keymap_right_joystick = new()
  {
    { "dpad_north", "DRJOYUP" },
    { "dpad_south", "DRJOYDOWN" },
    { "dpad_west", "DRJOYLEFT" },
    { "dpad_east", "DRJOYRIGHT" },
    { "click", "RSTICK" },
  };

  // these are found in "group_source_bindings"
  readonly static HashSet<string> supported_keys_digital = [
    "switch",
    "button_diamond",
    "dpad",
  ];
  readonly static HashSet<string> supported_keys_triggers = [
    "left_trigger",
    "right_trigger",
  ];
  readonly static HashSet<string> supported_keys_joystick = [
    "joystick",
    "right_joystick",
    "dpad",
  ];

  void SaveController()
  {
    var supportedCons = appInfoModel.ControllerInfo
      .Where(con =>
        supported_controllers_types.Contains(con.ControllerType.ToLowerInvariant())
        && con.EnabledBranches.Any(br => br.Equals("default", StringComparison.OrdinalIgnoreCase))
      );
    if (!supportedCons.Any())
    {
      return;
    }

    var con = supportedCons.First().VdfData;
    var presets_actions_bindings = ParseControllerVdfObj(con);

    if (presets_actions_bindings.Count > 0)
    {
      Directory.CreateDirectory(controllerFolder);
      foreach (var (presetName, presetObj) in presets_actions_bindings)
      {
        List<string> filecontent = [];
        foreach (var (actionName, actionBindingsSet) in presetObj)
        {
          filecontent.Add($"{actionName}={string.Join(',', actionBindingsSet)}");
        }

        var filepath = Path.Combine(controllerFolder, $"{presetName}.txt");
        File.WriteAllLines(filepath, filecontent, Utils.Utf8EncodingNoBom);
      }
    }

  }

  public static Dictionary<string, Dictionary<string, HashSet<string>>> ParseControllerVdfObj(JsonObject con)
  {
    var controller_mappings = con.GetKeyIgnoreCase("controller_mappings").ToObjSafe();

    var groups = controller_mappings.GetKeyIgnoreCase("group").ToVdfArraySafe();
    var actions = controller_mappings.GetKeyIgnoreCase("actions").ToVdfArraySafe();
    var presets = controller_mappings.GetKeyIgnoreCase("preset").ToVdfArraySafe();

    // each group defines the controller key and its binding
    var groups_by_id = groups
      .Select(g => new KeyValuePair<uint, JsonObject>(
        (uint)g.GetKeyIgnoreCase("id").ToNumSafe(), g.ToObjSafe()
      ))
      .ToDictionary(kv => kv.Key, kv => kv.Value)
      .AsReadOnly();

    // list of supported actions
    /*
     * ex:
     * "actions": {
     *   "ui": { ... },
     *   "driving": { ... }
     * },
     */
    var supported_actions_list = actions
      .SelectMany(a => a.ToObjSafe())
      .Select(kv => kv.Key.ToUpperInvariant())
      .ToHashSet();

    /*
     * ex:
     * {   preset name
     *     /
     *   "ui": {
     *     "ui_advpage0": ["LJOY=joystick_move"],
     *        ^                 ^
     *       action name        ^
     *                         action bindings set
     *     ...
     *     ...
     *   },
     *   
     *   "driving": {
     *     "driving_abackward": ["LBUMPER"],
     *     ...
     *     ...
     *   },
     *   
     *   ...
     *   ...
     * }
     */
    var presets_actions_bindings = new Dictionary<string, Dictionary<string, HashSet<string>>>();

    /*
     * "id": 0,
     * "name": "ui",
     * "group_source_bindings": {
     *   "13": "switch active",
     *   "16": "right_trigger active",
     *   "65": "joystick active",
     *   "15": "left_trigger active",
     *   "60": "right_joystick inactive",
     *   "66": "right_joystick active"
     * }
     * 
     * each preset has:
     *   1. action name, could be in the previous list or a standalone/new one
     *   2. the key bindings (groups) of this preset
     *      * each key binding entry is a key-value pair:
     *        <group ID> - <button_name SPACE active/inactive>
     *  also notice how the last 2 key-value pairs define the same "right_joystick",
     *  but one is active (ID=66) and the other is inactive (ID=60)
     */
    foreach (var presetObj in presets)
    {
      var preset_name = presetObj.GetKeyIgnoreCase("name").ToStringSafe();
      // find this preset in the parsed actions list
      if (!supported_actions_list.Contains(preset_name.ToUpperInvariant()) && !preset_name.Equals("default", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      var group_source_bindings = presetObj.GetKeyIgnoreCase("group_source_bindings").ToObjSafe();
      var bindings_map = new Dictionary<string, HashSet<string>>();
      foreach (var group_source_binding_kv in group_source_bindings)
      {
        uint group_number = 0;
        if (!uint.TryParse(group_source_binding_kv.Key, CultureInfo.InvariantCulture, out group_number)
            || !groups_by_id.ContainsKey(group_number))
        {
          Log.Instance.Write(Log.Kind.Debug, $"group_source_bindings with ID '{group_source_binding_kv.Key}' has bad number");
          continue;
        }

        var group_source_binding_elements = group_source_binding_kv.Value.ToStringSafe()
          .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
          .Select(str => str.Trim())
          .ToArray();
        /*
         * "group_source_bindings": {
         *   "10": "switch active",
         *   "11": "button_diamond active",
         *   "12": "left_trigger inactive",
         *   "18": "left_trigger active",
         *   "13": "right_trigger inactive",
         *   "19": "right_trigger active",
         *   "14": "right_joystick active",
         *   "15": "dpad inactive",
         *   "16": "dpad active",
         *   "17": "joystick active",
         *   "21": "left_trackpad active",
         *   "20": "right_trackpad active"
         * }
         */
        if (group_source_binding_elements.Length < 2 || !group_source_binding_elements[1].Equals("active", StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        // ex: "button_diamond", "right_trigger", "dpad" ...
        var btn_name_lower = group_source_binding_elements[0].ToLowerInvariant();
        if (supported_keys_digital.Contains(btn_name_lower))
        {
          var group = groups_by_id[group_number];
          AddInputBindings(bindings_map, group, keymap_digital);
        }

        if (supported_keys_triggers.Contains(btn_name_lower))
        {
          var group = groups_by_id[group_number];
          string group_mode = group.GetKeyIgnoreCase("mode").ToStringSafe();
          if (group_mode.Equals("trigger", StringComparison.OrdinalIgnoreCase))
          {
            foreach (var groupProp in group)
            {
              /*
               * "group": [
               *   {
               *     "id": 36,
               *     "mode": "trigger",
               *     "inputs": {
               *       ...
               *     }
               *     ...
               *     "gameactions": {
               *       "driving": "driving_abackward"
               *     }               ^
               *                     ^
               *     ...           action name
               *   }
               */
              if (groupProp.Key.Equals("gameactions", StringComparison.OrdinalIgnoreCase))
              {
                // ex: action_name = "driving_abackward"
                var action_name = groupProp.Value.GetKeyIgnoreCase(preset_name).ToStringSafe();
                string binding;
                if (btn_name_lower.Equals("left_trigger", StringComparison.OrdinalIgnoreCase))
                {
                  binding = "LTRIGGER";
                }
                else
                {
                  binding = "RTRIGGER";
                }

                string binding_with_trigger = $"{binding}=trigger";
                if (bindings_map.TryGetValue(action_name, out var action_set))
                {
                  if (!action_set.Contains(binding) && !action_set.Contains(binding_with_trigger))
                  {
                    action_set.Add(binding);
                  }
                }
                else
                {
                  bindings_map[action_name] = [binding_with_trigger];
                }
              }
              else if (groupProp.Key.Equals("inputs", StringComparison.OrdinalIgnoreCase))
              {
                string binding;
                if (btn_name_lower.Equals("left_trigger", StringComparison.OrdinalIgnoreCase))
                {
                  binding = "DLTRIGGER";
                }
                else
                {
                  binding = "DRTRIGGER";
                }
                AddInputBindings(bindings_map, group, keymap_digital, binding);
              }
            }
          }
          else
          {
            Log.Instance.Write(Log.Kind.Debug, $"group with ID [{group_number}] has unknown trigger mode '{group_mode}'");
          }
        }

        if (supported_keys_joystick.Contains(btn_name_lower))
        {
          var group = groups_by_id[group_number];
          string group_mode = group.GetKeyIgnoreCase("mode").ToStringSafe();
          if (group_mode.Equals("joystick_move", StringComparison.OrdinalIgnoreCase))
          {
            foreach (var groupProp in group)
            {
              /*
               * "group": [
               *   {
               *     "id": 36,
               *     "mode": "trigger",
               *     "inputs": {
               *       ...
               *     }
               *     ...
               *     "gameactions": {
               *       "driving": "driving_abackward"
               *     }
               *     ...
               *   }
               */
              if (groupProp.Key.Equals("gameactions", StringComparison.OrdinalIgnoreCase))
              {
                var action_name = groupProp.Value.GetKeyIgnoreCase(preset_name).ToStringSafe();
                string binding;
                if (btn_name_lower.Equals("joystick", StringComparison.OrdinalIgnoreCase))
                {
                  binding = "LJOY";
                }
                else if (btn_name_lower.Equals("right_joystick", StringComparison.OrdinalIgnoreCase))
                {
                  binding = "RJOY";
                }
                else
                {
                  binding = "DPAD";
                }

                string binding_with_joystick = $"{binding}=joystick_move";
                if (bindings_map.TryGetValue(action_name, out var action_set))
                {
                  if (!action_set.Contains(binding) && !action_set.Contains(binding_with_joystick))
                  {
                    action_set.Add(binding);
                  }
                }
                else
                {
                  bindings_map[action_name] = [binding_with_joystick];
                }
              }
              else if (groupProp.Key.Equals("inputs", StringComparison.OrdinalIgnoreCase))
              {
                string binding;
                if (btn_name_lower.Equals("joystick", StringComparison.OrdinalIgnoreCase))
                {
                  binding = "LSTICK";
                }
                else
                {
                  binding = "RSTICK";
                }
                AddInputBindings(bindings_map, group, keymap_digital, binding);
              }
            }
          }
          else if (group_mode.Equals("dpad", StringComparison.OrdinalIgnoreCase))
          {
            if (btn_name_lower.Equals("joystick", StringComparison.OrdinalIgnoreCase))
            {
              AddInputBindings(bindings_map, group, keymap_left_joystick);
            }
            else if (btn_name_lower.Equals("right_joystick", StringComparison.OrdinalIgnoreCase))
            {
              AddInputBindings(bindings_map, group, keymap_right_joystick);
            }
            else // dpad 
            {

            }
          }
          else
          {
            Log.Instance.Write(Log.Kind.Debug, $"group with ID [{group_number}] has unknown joystick mode '{group_mode}'");
          }
        }

      }

      presets_actions_bindings[preset_name] = bindings_map;
    }

    return presets_actions_bindings;

  }

  static void AddInputBindings(Dictionary<string, HashSet<string>> actions_bindings, JsonObject group, IReadOnlyDictionary<string, string> keymap, string? forced_btn_mapping = null)
  {
    var inputs = group.GetKeyIgnoreCase("inputs").ToVdfArraySafe();
    foreach (var inputObj in inputs)
    {
      foreach (var btnKv in inputObj.ToObjSafe()) // "left_bumper", "button_back_left", ...
      {
        foreach (var btnObj in btnKv.Value.ToVdfArraySafe())
        {
          foreach (var btnPropKv in btnObj.ToObjSafe()) // "activators", ...
          {
            foreach (var btnPropObj in btnPropKv.Value.ToVdfArraySafe())
            {
              foreach (var btnPressTypeKv in btnPropObj.ToObjSafe()) // "Full_Press", ...
              {
                foreach (var btnPressTypeObj in btnPressTypeKv.Value.ToVdfArraySafe())
                {
                  foreach (var pressTypePropsKv in btnPressTypeObj.ToObjSafe()) // "bindings", ...
                  {
                    foreach (var pressTypePropsObj in pressTypePropsKv.Value.ToVdfArraySafe())
                    {
                      foreach (var bindingKv in pressTypePropsObj.ToObjSafe()) // "binding", ...
                      {
                        if (!bindingKv.Key.Equals("binding", StringComparison.OrdinalIgnoreCase))
                        {
                          continue;
                        }

                        /*
                         * ex1:
                         * "binding": [
                         *   "game_action ui ui_advpage0, Route Advisor Navigation Page",
                         *   "game_action ui ui_mapzoom_out, Map Zoom Out"
                         * ]   ^          ^       ^
                         *     ^       category   ^
                         *     type               action name
                         * 
                         * ex2:
                         * "binding": [
                         *   "xinput_button TRIGGER_LEFT, Brake/Reverse"
                         * ]   ^              ^
                         *     ^              ^
                         *     type           action name
                         * 
                         * 1. split and trim each string => string[]
                         * 2. save each string[]         => List<string[]>
                         */
                        // each string is composed of:
                        //   1. binding type, ex: "game_action", "xinput_button", ...
                        //   2. (optional) action category, ex: "ui", should be from one of the previously parsed action list
                        //   3. action name, ex: "ui_mapzoom_out" or "TRIGGER_LEFT"

                        string current_btn_name = btnKv.Key; // "left_bumper", "button_back_left", ...

                        var binding_instructions_lists = bindingKv.Value
                          .ToVdfArraySafe()
                          .Select(obj => obj.ToStringSafe())
                          .Select(str =>
                            str.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(element => element.Trim())
                               .ToArray()
                          )
                          .Where(list => list.Length > 1); // we need at least the instruction

                        //Log.Instance.Write(Log.Kind.Debug, $"button '{current_btn_name}' has [{binding_instructions_lists.Count()}] binding instructions (group/.../activators/Full_Press/bindings/binding/[ <INSTRUCTIONS_LIST> ])");

                        foreach (var binding_instructions in binding_instructions_lists)
                        {
                          var binding_type = binding_instructions[0];
                          string? action_name = null;
                          if (binding_type.Equals("game_action", StringComparison.OrdinalIgnoreCase) && binding_instructions.Length >= 3)
                          {
                            action_name = binding_instructions[2]; // ex: "ui_mapzoom_out,"
                          }
                          else if (binding_type.Equals("xinput_button", StringComparison.OrdinalIgnoreCase) && binding_instructions.Length >= 2)
                          {
                            action_name = binding_instructions[1]; // ex: "TRIGGER_LEFT,"
                          }

                          if (action_name is null)
                          {
                            Log.Instance.Write(Log.Kind.Debug, $"unsupported binding type '{binding_type}' in button '{current_btn_name}' (group/.../activators/Full_Press/bindings/binding/['<BINDING_TYPE> ...'])");
                            continue;
                          }

                          if (action_name.Last() == ',')
                          {
                            action_name = action_name.Substring(0, action_name.Length - 1);
                          }

                          string? btn_binding = null;
                          if (forced_btn_mapping is null)
                          {
                            if (keymap.TryGetValue(current_btn_name.ToLowerInvariant(), out var mapped_btn_binding))
                            {
                              btn_binding = mapped_btn_binding;
                            }
                            else
                            {
                              Log.Instance.Write(Log.Kind.Debug, $"keymap is missing button '{current_btn_name}'");
                              continue;
                            }
                          }
                          else
                          {
                            btn_binding = forced_btn_mapping;
                          }

                          if (btn_binding is not null)
                          {
                            HashSet<string>? action_bindings_set;
                            if (!actions_bindings.TryGetValue(action_name, out action_bindings_set))
                            {
                              action_bindings_set = [];
                              actions_bindings[action_name] = action_bindings_set;
                            }

                            action_bindings_set.Add(btn_binding);
                          }
                          else
                          {
                            Log.Instance.Write(Log.Kind.Debug, $"missing keymap for btn '{current_btn_name}' (group/inputs/<BTN_NAME>)");
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
      }
    }

  }

  void SaveInventory()
  {
    if (appInfoModel.InventoryItems.OriginalSchema.Count == 0)
    {
      return;
    }

    JsonObject invItems = new();
    JsonObject defaultInvItems = new();
    foreach (var item in appInfoModel.InventoryItems.OriginalSchema)
    {
      var itemObj = item.ToObjSafe();
      var itemId = itemObj.GetKeyIgnoreCase("itemdefid").ToStringSafe();
      if (string.IsNullOrEmpty(itemId))
      {
        continue;
      }

      JsonObject outItem = new();
      invItems[itemId] = outItem;
      defaultInvItems[itemId] = 1;

      foreach (var (key, val) in itemObj)
      {
        JsonNode outVal;
        if (val is null)
        {
          outVal = "null";
        }
        else
        {
          switch (val.GetValueKind())
          {
            case JsonValueKind.True:
              outVal = "true";
              break;
            case JsonValueKind.False:
              outVal = "false";
              break;
            case JsonValueKind.Null:
              outVal = "null";
              break;
            default:
              outVal = val.ToString();
              break;
          }
        }

        outItem[key] = outVal;
      }
    }

    Directory.CreateDirectory(settingsFolder);
    Utils.WriteJson(invItems, Path.Combine(settingsFolder, "items.json"));
    Utils.WriteJson(defaultInvItems, Path.Combine(settingsFolder, "default_items.json"));
  }

  Task SaveAchievements()
  {
    if (appInfoModel.StatsAndAchievements.Achievements.Count == 0)
    {
      return Task.CompletedTask;
    }

    bool needDefaultIconUnlocked = false;
    bool needDefaultIconLocked = false;

    List<JsonObject> achs = [];
    foreach (var ach in appInfoModel.StatsAndAchievements.Achievements.OrderBy(ach => ach.Id))
    {
      string iconUnlockedName;
      if (string.IsNullOrEmpty(ach.IconUnlocked.Name))
      {
        iconUnlockedName = DEFAULT_ACH_ICON_RSRC_UNLOCKED;
        needDefaultIconUnlocked = true;
      }
      else
      {
        iconUnlockedName = ach.IconUnlocked.NameOnDisk;
      }

      string iconLockedName;
      if (string.IsNullOrEmpty(ach.IconLocked.Name))
      {
        iconLockedName = DEFAULT_ACH_ICON_RSRC_LOCKED;
        needDefaultIconLocked = true;
      }
      else
      {
        iconLockedName = ach.IconLocked.NameOnDisk;
      }

      var obj = new JsonObject
      {
        ["name"] = ach.InternalName,
        ["hidden"] = ach.IsHidden ? 1 : 0,

        ["icon"] = $"{ACHIEVEMENT_IMAGE_FOLDER_NAME}/{iconUnlockedName}",
        ["icon_gray"] = $"{ACHIEVEMENT_IMAGE_FOLDER_NAME}/{ACHIEVEMENT_IMAGE_LOCKED_FOLDER_NAME}/{iconLockedName}",

        ["displayName"] = ach.FriendlyNameTranslations.DeepClone(),
        ["description"] = ach.DescriptionTranslations.DeepClone(),

      };
      if (ach.ProgressDetails is not null)
      {
        obj["progress"] = ach.ProgressDetails.DeepClone();
      }

      achs.Add(obj);
    }

    Directory.CreateDirectory(settingsFolder);
    Utils.WriteJson(achs, Path.Combine(settingsFolder, "achievements.json"));

    if (needDefaultIconUnlocked )
    {
      Log.Instance.Write(Log.Kind.Debug, $"one of the achievements is missing unlocked icon, adding default one");
      Directory.CreateDirectory(achievementsImagesFolder);
      var myType = typeof(GseGenerator);
      var iconRsrcName = $"{myType.Namespace}.Assets.{DEFAULT_ACH_ICON_RSRC_UNLOCKED}";
      var iconStream = myType.Assembly.GetManifestResourceStream(iconRsrcName)
        ?? throw new InvalidDataException($"missing resource for default unlocked achievement icon");
      using var fs = new FileStream(Path.Combine(achievementsImagesFolder, DEFAULT_ACH_ICON_RSRC_UNLOCKED), FileMode.Create, FileAccess.Write);
      iconStream.CopyTo(fs);
    }

    if (needDefaultIconLocked)
    {
      Log.Instance.Write(Log.Kind.Debug, $"one of the achievements is missing locked icon, adding default one");
      Directory.CreateDirectory(achievementsImagesLockedFolder);
      var myType = typeof(GseGenerator);
      var iconRsrcName = $"{myType.Namespace}.Assets.{DEFAULT_ACH_ICON_RSRC_LOCKED}";
      var iconStream = myType.Assembly.GetManifestResourceStream(iconRsrcName)
        ?? throw new InvalidDataException($"missing resource for default locked achievement icon");
      using var fs = new FileStream(Path.Combine(achievementsImagesLockedFolder, DEFAULT_ACH_ICON_RSRC_LOCKED), FileMode.Create, FileAccess.Write);
      iconStream.CopyTo(fs);
    }

    var iconsUnlocked = appInfoModel.StatsAndAchievements.Achievements.Select(ach => ach.IconUnlocked);
    var iconsLocked = appInfoModel.StatsAndAchievements.Achievements.Select(ach => ach.IconLocked);

    Task iconsUnlockedTask = Task.CompletedTask;
    Task iconsLockedTask = Task.CompletedTask;

    if (iconsUnlocked.Any(ico => ico.Data.Count > 0))
    {
      Directory.CreateDirectory(achievementsImagesFolder);
      iconsUnlockedTask = Utils.ParallelJobsAsync(iconsUnlocked, async (item, _, _, ct) =>
      {
        if (item.Data.Count == 0)
        {
          return;
        }

        var filepath = Path.Combine(achievementsImagesFolder, item.NameOnDisk);
        await File.WriteAllBytesAsync(filepath, [.. item.Data], ct).ConfigureAwait(false);
      }, 30, 5);
    }
    
    if (iconsLocked.Any(ico => ico.Data.Count > 0))
    {
      Directory.CreateDirectory(achievementsImagesLockedFolder);
      iconsLockedTask = Utils.ParallelJobsAsync(iconsLocked, async (item, _, _, ct) =>
      {
        if (item.Data.Count == 0)
        {
          return;
        }

        var filepath = Path.Combine(achievementsImagesLockedFolder, item.NameOnDisk);
        await File.WriteAllBytesAsync(filepath, [.. item.Data], ct).ConfigureAwait(false);
      }, 30, 5);
    }

    return Task.WhenAll(iconsUnlockedTask, iconsLockedTask);
  }

}
