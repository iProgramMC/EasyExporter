# EasyExporter
This is a level exporter for *Super Mario 64*. This converts OBJ files into a format that can (essentially) just be dropped in to an SM64 decomp
repository. It requires some knowledge of how levels work.

**NOTE**: This is not as easy as the title says. I decided to keep it because it would break existing links, though.

## TODOs/Issues:
- The model does not automatically triangulate models. If you have N-gons, the tool will always pick the first 3 vertices in the n-gon, as defined in the obj
- Does not support model formats other than OBJ
- The tool does not support textures whose width and height are not powers of 2.
- The tool does not like huge textures, it takes a long time to convert them to text
- The tool does not resize images automatically to fit into the N64's constraints (to 64x32, for example)
- The tool does not use CI4/CI8 texture modes
- The tool exports textures in u16 format, not u8, so models exported may not work on SM64 source ports
- The tool does not include converted PNG files, but embeds them directly in the model file
- No automatic level folder generation
- To change the collision of a model, aside from a few cases built into the tool based on the material's name, manual editing of the output collision file is required.

## Warning
If you do not know how a level is structured, please use Fast64 instead. This was designed for more intricate level replacement operations,
and you will need to do a lot of stuff manually.

## How it works
This takes any \*.obj file and converts it into a usable format for decomp.

### How to import the levels
This should generate a `convert.exe`. You can run it with the following arguments:
`convert <your obj file> [output name = "output"] [-sum (defaults to false)] [-noscale (scale defaults to 100)] [-nuf (defaults to active)]`

Example usage: `convert my_new_level.obj course -fe`. The tool will generate two files, `my_new_level.obj_outputCollision.inc.c` and `(..)outputModel.inc.c`.

**NOTE:** Your model **MUST** be triangulated. The tool will not triangulate the model.

`-sum` stands for SketchUp Mode, which is actually a misnomer, as it's actually a mode for the LIPID OBJ exporter for SketchUp (This one automatically triangulates, so no need to worry). It flips the Y and Z axes around, because LIPID didn't do it.

`-nuf` stands for No UV Fix, which disables the UV fixer. If you ever encounter issues with the model because of UV overflow fixer, you can turn it off.

`-fe` is the **recommended** option, pretty much always. Centers all the UV faces so that the median position is between the range of `[0, 1)` on both X and Y axes. This makes sure the output model looks just like the source model.

`-noscale`.. You know what it does. If you made the model at the native SM64 scale (so 1 unit in your modelling program equals 1 unit in game) you can use this to keep the scaling native. 

`-f64s`. Sets the scaling factor to the default scaling factor of Fast64.

Note that if the model is made for a scaling factor that isn't 100 or 1, you will have to resize your model to be in one of these scaling.

The output name must be specified for `-sum` to work. It may only have characters from `A-Z`, `a-z`, `0-9` and `_` and may not start with a digit.

Afterwards, you will see a few new files:
`<file>_outputCollision.inc.c`
`<file>_outputModel.inc.c`

To replace a level with the newly created file, simply paste the contents into `levels/<your level>/areas/<your area>/collision.inc.c` and `levels/<your level>/areas/<your area>/1/model.inc.c`
respectively. But don't build yet! There's more.

### Geo Layouts
Usually, the exporter creates two display lists: `<output>_main_display_list_opaque` and `<output>_main_display_list_alpha`. The way I use it usually is just replacing what the level geo draws to my stuff, and that's what I recommend. However, if you'd like to start from scratch, here's an example, which replaces CotMC with your custom level, and uses `output` as its name.
```c
extern const Gfx output_main_display_list_opaque[];
extern const Gfx output_main_display_list_alpha[];
const GeoLayout cotmc_geo_0001A0[] = {
   GEO_NODE_SCREEN_AREA(10, SCREEN_WIDTH/2, SCREEN_HEIGHT/2, SCREEN_WIDTH/2, SCREEN_HEIGHT/2),
   GEO_OPEN_NODE(),
      GEO_ZBUFFER(0),
      GEO_OPEN_NODE(),
         GEO_NODE_ORTHO(100),
         GEO_OPEN_NODE(),
            GEO_BACKGROUND_COLOR(0x0001), // Change this to reflect changes in the background.
         GEO_CLOSE_NODE(),
      GEO_CLOSE_NODE(),
      GEO_ZBUFFER(1),
      GEO_OPEN_NODE(),
         GEO_CAMERA_FRUSTUM_WITH_FUNC(45, 100, 12800, geo_camera_fov),
         GEO_OPEN_NODE(),
            GEO_CAMERA(16, 0, 2000, 6000, 0, 0, 0, geo_camera_main),
            GEO_OPEN_NODE(),
               GEO_DISPLAY_LIST(LAYER_OPAQUE, output_main_display_list_opaque), // <-+- We're including our own stuff
               GEO_DISPLAY_LIST(LAYER_ALPHA, output_main_display_list_alpha),   // <-+
               GEO_RENDER_OBJ(),
               GEO_ASM(0, geo_envfx_main),
            GEO_CLOSE_NODE(),
         GEO_CLOSE_NODE(),
      GEO_CLOSE_NODE(),
   GEO_CLOSE_NODE(),
   GEO_END(),
};
```

### Level script
We're still not done! One more thing... change the line where `TERRAIN()` is (in the desired area, of course) to `<outputname>_collision` in
`levels/<your level>/script.c` and change the previous name that there was to the new name in `header.h`.

### Notes
This level exporter is very flexible, although you will have to do a lot of things manually.

### Messed up UVs ingame?
Unless you've used `-nuf` (in which case, try removing it), it's most likely a problem with the source model itself. If you're using faces that are really
long or the UV shows the texture too many times over, the UV coordinates may overflow and the UV may look stretched.
