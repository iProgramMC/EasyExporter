# EasyExporter
This is a level exporter for *Super Mario 64*. Requires SM64 decomp and a knowledge of how levels work.

## Warning
If you do not know how a level is structured, please use Fast64 instead. This is meant as a more advanced version, and you will need to do a lot of stuff manually.

## How it works
This takes any \*.obj file and converts it into a usable format for decomp.

### How to import the levels
This should generate a `convert.exe`. You can run it with the following arguments:
`convert <your obj file> [output name = "output"] [-sum (defaults to false)]`

**NOTE:** Your model **MUST** be triangulated. The tool will not triangulate the model.

`-sum` stands for SketchUp Mode, which is actually a misnomer, as it's actually a mode for the LIPID OBJ exporter for SketchUp (This one automatically triangulates, so need to worry). It flips the Y and Z axes around, because LIPID didn't do it.

The output name must be specified for `-sum` to work. It may only have characters from `A-Z`, `a-z`, `0-9` and `_` and may not start with a digit.

Afterwards, you will see a few new files:
`<file>_outputCollision.inc.c`
`<file>_outputModel.inc.c`

To replace a level with the newly created file, simply paste the contents into `levels/<your level>/areas/<your area>/collision.inc.c` and `levels/<your level>/areas/<your area>/1/model.inc.c` respectively. But don't build yet! There's more.

### Geo Layouts
Usually, the exporter creates two display lists: `<output>_main_display_list_opaque` and `<output>_main_display_list_alpha`. Here's an example, which replaces CotMC with your custom level, and uses `output` as its name.
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
               GEO_DISPLAY_LIST(LAYER_OPAQUE, output_main_display_list_opaque),
               GEO_DISPLAY_LIST(LAYER_ALPHA, output_main_display_list_alpha),
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
We're still not done! One more thing... change the line where `TERRAIN()` is (in the desired area, of course) to `<outputname>_collision` in `levels/<your level>/script.c` and change the previous name that there was to the new name in `header.h`.

### Notes
This level exporter is very flexible. Although you will have to do a lot of things manually, you can import any \*.obj file.

### Messed up UVs ingame?
Might be that LIPID makes UV vertices that are really far away. 
##### If you aren't using Lipid
Make sure that no UV vertices' positions go beyond the range `[-16,15]`.
##### If you are
Please import the model in Blender, then do the steps detailed above, and reexport (From Blender, of course)
