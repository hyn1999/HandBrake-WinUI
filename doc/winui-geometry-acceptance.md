# Manual geometry with inherited rotation and borders

## Problem and change

The previous post-resolution overlay removed CROP_SCALE, ROTATE and PAD, inserted a manually generated scale filter and replaced PAR with the unrotated source PAR. Manual dimensions therefore silently removed inherited rotation/flip/borders and could use the wrong pixel aspect after a quarter turn.

Manual dimensions/crop now modify the native preset **before** CreatePresetJob. The native dimension resolver generates the complete geometry filter chain. PictureRotate, PicturePadMode, border values and colour remain preset input. The effect-filter editor continues to retain structural filters.

Manual width/height describe scaled image pixels before borders, and crop values describe the source after preset rotation/flip. Source PAR is retained, inverted at 90/270 degrees. Manual geometry explicitly uses custom PAR, independent width/height, modulus 2 and permits the requested upscaling. Ordinary preset resolution limits are replaced by the manual dimensions. Fill/letterbox/pillarbox limits that define a border canvas remain; a requested image larger than that canvas is rejected. Custom edge borders are resolved by the engine from the new image size.

After native resolution, CROP_SCALE must match all six requested dimensions/crop values exactly. Native minimum-size, modulus or canvas adjustments result in an actionable error instead of silently using another size. This check does not claim native encode success.

The manual editor initializes rotated dimensions and crop edges through the existing HandBrakePictureHelpers.RotateGeometry wrapper used by WPF. No engine source was changed. Both still-preview and encode already use BuildNativeJob, so the next decoded frame and encode use the same geometry mapping. This change does not add live preview refresh while typing.

## Source evidence

Reference snapshot: 6986dba88a4e23d47b66d586216c7ba3f8c61113.

- libhb/preset.c, hb_preset_apply_dimensions: rotation precedes source geometry/crop resolution; custom crop mode is 3; forced dimensions, custom PAR and pad mode feed hb_set_anamorphic_size2; borders are derived from resolved scale settings.
- libhb/hb.c, hb_rotate_geometry and hb_set_anamorphic_size2: horizontal flip then rotation transforms crop; quarter turns swap dimensions and PAR numerator/denominator; forced-size/aspect/upscale rules determine resolved geometry.
- HandBrake.Interop/Interop/HandBrakePictureHelpers.cs: RotateGeometry wraps hb_rotate_geometry and returns oriented dimensions/PAR/crop.

The pinned WPF parity audit is historical. This implements its PIC-01 preservation step; full anamorphic/scaling/rotate/padding editors remain outstanding.

## Validation performed

Source contract review against the above native implementations and Interop picture model signatures; XAML XML parsing; verification that manual input is applied before native preset resolution and the destructive post-resolution geometry replacement is removed. No .NET compilation, Windows/native execution or build was performed.

## Pending Windows checks for the aggregate build

1. 1920×1080, square PAR, no rotation/pad, manual 1280×720: one matching CROP_SCALE, unchanged effects and 1:1 PAR.
2. 1920×1080 with 90/270 rotation: initialize editor at 1080×1920; test a crop valid only in the rotated coordinates. Retain ROTATE and ensure no duplicate structural filters.
3. Non-square source PAR 4:3 with 90/270 rotation: output PAR must reduce to 3:4. With 0/180 it remains 4:3. Test horizontal flip with asymmetric source crops against WPF's helper.
4. Custom border edges and colour with manual scaling: retain the border settings; resulting storage size must include the borders around the newly scaled image. Test both preset and manual effect-filter modes.
5. Fill, letterbox and pillarbox: retain the prescribed canvas on the relevant axes; reject a larger requested image rather than cropping/resizing it silently.
6. Unsupported tiny dimensions and a crop removing the entire rotated source must fail with a visible error; neither preview nor encode may silently change the requested size.
7. Decode the next still frame and encode a short CPU clip from the same task. Verify orientation, crop, borders, storage size, display aspect and output readability with non-square PAR as well as square PAR.
8. Queue → Edit → Save with geometry: resolved-job equality must permit an unchanged supported task; changing only dimensions must preserve rotation, border colour/edges, range, tracks and other task fields.

The prior native error 3 still needs a reproduced attempt/log diagnosis. This geometry correction is not a claim to fix it.
