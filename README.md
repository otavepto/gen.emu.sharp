# gen.emu.sharp
A rewrite of the [generate_emu_config](https://gitlab.com/Mr_Goldberg/goldberg_emulator/-/tree/master/scripts) tool by Mr.Goldberg in C#, credits to them for the original tool.  

## Changes
* Login credentials are retained for later login attempts inside the folder `credentials/`, avoiding the need to login each time the tool is used
* Generate all possbile schemas for [Achievement Watcher](https://github.com/xan105/Achievement-Watcher) by xan105
* Automatically find the IDs of top reviewers and use them to query for the stats/achievements schema
* Extend built-in owners IDs by adding them to a file called `top_owners_ids.txt`, each ID on a separate line
* Download various media assets for each app: backgrounds, icons, screenshots, inventory items images, ...

## Example
* console.gen.emu.cfg
  ```shell
  console.gen.emu.cfg 420 730 227300 -v --icons --imgs --scrn --thumbs --vid --invicons --inviconslarge
  ```
* console.vdf.parser
  ```shell
  console.vdf.parser path/to/file_1.vdf path/to/file_2.vdf 
  ```
* console.stats.schema
  ```shell
  console.stats.schema path/to/UserGameStatsSchema_480.bin path/to/UserGameStatsSchema_730.bin
  ```

## Help page
```shell
console.gen.emu.cfg --help
```

## Third-party credits
* [SteamKit](https://github.com/SteamRE/SteamKit)
* [ValveKeyValue](https://github.com/ValveResourceFormat/ValveKeyValue)
* [CommandLineParser](https://github.com/commandlineparser/commandline)
