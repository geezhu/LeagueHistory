# WeGame honor icons

Downloaded on 2026-09-08 from Tencent's public WeGame/TGP website. These are original sprite pixels, not redrawn icons.

- Sprite: https://cdn.tgp.qq.com/lol/images/common/honor16.c34f6b6f98d22e92120b566617344ed1.png
- Coordinates: https://cdn.tgp.qq.com/lol/css/common.css (`.honor16-*`, 16 x 16 cells)
- Meanings: https://wegame.gtimg.com/lol/js/common/lolutils.ac95205a4645739ea2ce64025cb91ced.js (`getBattleTagIconListHTML`)
- Page: https://www.wegame.com.cn/lol/profile/v1602/history.shtml

This is an older public resource set; it is not guaranteed to match every icon in the current desktop client. Public access does not imply an open-source asset license. Artwork remains owned by its respective rights holders.

Used mappings: assists `(16,0)`, gold `(48,0)`, damage taken `(0,16)`, champion damage `(16,16)`, triple kill `(32,16)`, quadra kill `(0,0)`, penta kill `(16,32)`, turrets `(48,16)`, minions `(48,32)`.

The public JS maps server-provided battle tags to icons; it does not document how all those tags are awarded. This application explicitly labels non-kill badges as match-wide maxima calculated from complete participant fields, including ties. No MVP/SVP, AFK, or proprietary rating is inferred. No double-kill or most-kills badge is shown. Only the highest multi-kill badge is displayed, matching the public JS display behavior.

No verified first-blood icon was found in this set. First blood uses a locally drawn flat blood-drop symbol, explicitly separate from the WeGame sprite mappings. Other badges retain their original 16-pixel raster size.
