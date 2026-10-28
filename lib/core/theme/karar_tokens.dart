import 'package:flutter/material.dart';

/// Karar design tokens (spec v1 "Yeni Tasarım", §2).
///
/// * Colours live in the [KararColors] theme extension (`context.kc`), with a
///   light and a dark set. Every text pair was measured for WCAG AA.
/// * Spacing, radii, shadows, glass parameters and brand gradients are
///   theme-independent constants ([KararSpace], [KararRadius], [KararShadow],
///   [KararGlass], [KararGradients]).
///
/// Vote colour usage matrix (enforce):
/// * filled button/capsule + white label → `hakliFill` / `haksizFill`
/// * icon on a white/off-white capsule → `hakliFill` / `haksizFill`
/// * text on light surfaces → `hakliText` / `haksizText` (light values)
/// * text on light capsules in BOTH themes → `onCapsuleHakli` / `onCapsuleHaksiz`
/// * text on dark surfaces → `hakliText` / `haksizText` (dark values)
/// Always pair a vote colour with the ✓ / ✕ glyph or the word.
@immutable
class KararColors extends ThemeExtension<KararColors> {
  const KararColors({
    required this.brightness,
    required this.canvas,
    required this.surface,
    required this.elevated,
    required this.muted,
    required this.outlineVariant,
    required this.outline,
    required this.text1,
    required this.text2,
    required this.text3,
    required this.primary,
    required this.primaryFill,
    required this.primaryText,
    required this.primaryContainer,
    required this.onPrimaryContainer,
    required this.navActive,
    required this.ink,
    required this.onInk,
    required this.hakliFill,
    required this.hakliText,
    required this.hakliSoft,
    required this.haksizFill,
    required this.haksizText,
    required this.haksizSoft,
    required this.gold,
    required this.goldText,
    required this.warnSoft,
    required this.warnText,
    required this.mint,
    required this.scrim,
    required this.capsule,
    required this.onCapsuleText,
    required this.onCapsuleMuted,
    required this.onCapsuleHakli,
    required this.onCapsuleHaksiz,
    required this.navPill,
    required this.sheet,
    required this.hairline,
    required this.shimmerBase,
    required this.shimmerHighlight,
    required this.raised,
    required this.fieldFill,
    required this.fieldBorder,
    required this.onColor,
    required this.onColorSoft,
    required this.onColorTrack,
    required this.splashOnColor,
    required this.highlightOnColor,
    required this.photoVeil,
    required this.quoteWatermark,
    required this.mediaBackdrop,
  });

  final Brightness brightness;

  /// Scaffold background: soft lavender off-white / near-black violet.
  final Color canvas;

  /// Cards, sheets (light), nav pill (light).
  final Color surface;

  /// Menus, dark nav, dark sheets and dialogs.
  final Color elevated;

  /// Chips, inputs, "Yanıtla" pill, tracks.
  final Color muted;

  /// Hairlines and dividers (decorative only).
  final Color outlineVariant;

  /// UI boundaries (≥ 3:1 vs surface).
  final Color outline;

  /// Primary text (18.3:1 / 16.4:1).
  final Color text1;

  /// Secondary text, comment bodies (6.93:1 / 8.74:1).
  final Color text2;

  /// Meta text and placeholders (≥ 4.97:1 on muted).
  final Color text3;

  /// Brand fills, icons, focus rings. Dark: lavender #A98BFF — never put
  /// white content on it; use [primaryFill] for white-on-brand.
  final Color primary;

  /// Brand CTA fill in both themes (#7D50F0, white text 4.91:1).
  final Color primaryFill;

  /// Purple text (#6438E0 light, #A98BFF dark).
  final Color primaryText;

  /// Brand tint (active nav tab, AI card, OP chip).
  final Color primaryContainer;
  final Color onPrimaryContainer;

  /// Active icon colour in navigation (light #7D50F0 / dark #B39AFF).
  final Color navActive;

  /// Strong fill for the nav notch, the side-nav "Oluştur" CTA, selected
  /// neutral chips, segmented/toggle selections, the coachmark bubble and
  /// ink buttons. Light: near-black ink #15131F. Dark: brand violet
  /// #7D50F0 (an inverse off-white block glared on the dark canvas), so
  /// [onInk] is white in both themes (18.3:1 / 4.91:1).
  final Color ink;
  final Color onInk;

  final Color hakliFill;
  final Color hakliText;
  final Color hakliSoft;
  final Color haksizFill;
  final Color haksizText;
  final Color haksizSoft;

  /// Weekly hero, streaks, star (decorative / on ink only).
  final Color gold;

  /// Gold-family text on light surfaces.
  final Color goldText;
  final Color warnSoft;
  final Color warnText;

  /// Decorative dot only — never behind text.
  final Color mint;

  /// Modal barrier.
  final Color scrim;

  /// Rail buttons, verdict chip and on-colour CTAs (onboarding "Devam").
  /// Light white; dark the spec §2.2 off-white #F4F3F8 — never a dimmed
  /// grey, which read as a disabled button on the vivid gradients
  /// (#15131F text 16.6:1, #5B576D 6.28:1, #116B34 5.99:1, #B4232A 5.92:1,
  /// #15803D icon 4.54:1, #D6363C icon 4.27:1, #7D50F0 icon 4.45:1).
  final Color capsule;

  /// Text on capsules — theme-independent (capsules are light in both themes).
  final Color onCapsuleText;
  final Color onCapsuleMuted;
  final Color onCapsuleHakli;
  final Color onCapsuleHaksiz;

  /// Floating nav pill fill (light white, dark #1F1C2A @ 94 %).
  final Color navPill;

  /// Bottom sheet / dialog background (light surface, dark elevated).
  final Color sheet;

  /// Dark-mode hairline used instead of shadows (transparent in light).
  final Color hairline;

  /// Skeleton shimmer colours.
  final Color shimmerBase;
  final Color shimmerHighlight;

  /// A surface raised above [muted] (segmented-control indicator, toggles
  /// on a muted track). Light white (with a soft shadow); dark #3A3552
  /// (+ hairline), lighter than the #262233 track so the selection never
  /// reads as a sunken hole. text1 on it: 18.3:1 / 10.5:1.
  final Color raised;

  /// Text field fill and enabled border. Light: white field with a 1 px
  /// #8E8AA3 border (3.04:1 on canvas, 3.33:1 on white) so fields are
  /// visible on the lavender canvas. Dark: #262233 fill with a 1 px #706B8A
  /// border — 3.86:1 on the canvas, 3.31:1 on sheets, 3.06:1 against its
  /// own fill — so dark fields keep a real boundary (the old #2F2B3D
  /// hairline was 1.42:1 and fields read as flat smudges).
  final Color fieldFill;
  final Color fieldBorder;

  /// White content on colour: gradients, photos, brand/vote fills (both
  /// themes). Use this instead of `Colors.white`.
  final Color onColor;

  /// White 25 %: discs/tiles on colour (anonymous avatar, icon tiles).
  final Color onColorSoft;

  /// White 14 %: pills and tracks on colour (weekly hero meta pills).
  final Color onColorTrack;

  /// White 12 % / 6 %: ink splash and highlight on colour surfaces.
  final Color splashOnColor;
  final Color highlightOnColor;

  /// Black 30 %: veil over photos (tile scrims, image overlays).
  final Color photoVeil;

  /// White 10 %: the 150 px “ watermark on gradient cards.
  final Color quoteWatermark;

  /// Full-screen media viewer background (black in both themes).
  final Color mediaBackdrop;

  /// Avatar disc colours (same in both themes). Every entry carries a
  /// white initial at ≥ 4.5:1 and avoids the Haklı green / Haksız red hues.
  List<Color> get avatarPalette => KararPalette.avatar;

  bool get isDark => brightness == Brightness.dark;

  /// Text colour for a vote side on the current theme's surfaces.
  Color voteText(bool hakli) => hakli ? hakliText : haksizText;

  /// Fill colour for a vote side (same in both themes).
  Color voteFill(bool hakli) => hakli ? hakliFill : haksizFill;

  /// Soft tonal background for a vote side.
  Color voteSoft(bool hakli) => hakli ? hakliSoft : haksizSoft;

  /// Icon colour for a vote side on surfaces and soft tints: the fill tone
  /// in light, the brighter text tone in dark (the fill tones drop to
  /// ~2.7–3.5:1 on dark surfaces and on the dark soft tints).
  Color voteIcon(bool hakli) => isDark ? voteText(hakli) : voteFill(hakli);

  /// Split-bar colour for a vote side (bar segments and progress tracks on
  /// surfaces; never under text). Light: the vote fill. Dark: halfway
  /// between fill and text tone — #30AF5F (6.4:1 on `surface`) and #EB585B
  /// (5.2:1), near-equal lightness, so the bar matches the bright labels
  /// beside it instead of a dull forest green next to a more vivid red.
  Color voteBar(bool hakli) => isDark
      ? Color.lerp(voteFill(hakli), voteText(hakli), 0.5)!
      : voteFill(hakli);

  Color get hakliBar => voteBar(true);
  Color get haksizBar => voteBar(false);

  /// Search-match highlight behind text on cards. Light: primaryContainer
  /// (#EEE8FD). Dark: primaryContainer was 1.20:1 on the card and the match
  /// vanished, so dark uses the brand fill at 55 % over `surface` (#4F3592,
  /// 1.94:1 against the card).
  Color get searchHighlightBg => isDark
      ? Color.alphaBlend(primaryFill.withValues(alpha: 0.55), surface)
      : primaryContainer;

  /// Text on [searchHighlightBg]: onPrimaryContainer (#4A22B5) in light,
  /// text1 in dark (8.4:1 on #4F3592).
  Color get searchHighlightFg => isDark ? text1 : onPrimaryContainer;

  static const light = KararColors(
    brightness: Brightness.light,
    canvas: Color(0xFFF5F4F9),
    surface: Color(0xFFFFFFFF),
    elevated: Color(0xFFFFFFFF),
    muted: Color(0xFFEFEDF5),
    outlineVariant: Color(0xFFE7E5EE),
    outline: Color(0xFF8E8AA3),
    text1: Color(0xFF15131F),
    text2: Color(0xFF5B576D),
    text3: Color(0xFF67637A),
    primary: Color(0xFF7D50F0),
    primaryFill: Color(0xFF7D50F0),
    primaryText: Color(0xFF6438E0),
    primaryContainer: Color(0xFFEEE8FD),
    onPrimaryContainer: Color(0xFF4A22B5),
    navActive: Color(0xFF7D50F0),
    ink: Color(0xFF15131F),
    onInk: Color(0xFFFFFFFF),
    hakliFill: Color(0xFF15803D),
    hakliText: Color(0xFF116B34),
    hakliSoft: Color(0xFFE4F5EA),
    haksizFill: Color(0xFFD6363C),
    haksizText: Color(0xFFB4232A),
    haksizSoft: Color(0xFFFDE8E8),
    gold: Color(0xFFF5C04E),
    goldText: Color(0xFF8A5A00),
    warnSoft: Color(0xFFFFF4D6),
    warnText: Color(0xFF92400E),
    mint: Color(0xFF57CA8C),
    scrim: Color(0x730A0814), // rgba(10,8,20,.45)
    capsule: Color(0xFFFFFFFF),
    onCapsuleText: Color(0xFF15131F),
    onCapsuleMuted: Color(0xFF5B576D),
    onCapsuleHakli: Color(0xFF116B34),
    onCapsuleHaksiz: Color(0xFFB4232A),
    navPill: Color(0xFFFFFFFF),
    sheet: Color(0xFFFFFFFF),
    hairline: Color(0x00FFFFFF),
    shimmerBase: Color(0xFFECEAF3),
    shimmerHighlight: Color(0xFFF8F7FB),
    raised: Color(0xFFFFFFFF),
    fieldFill: Color(0xFFFFFFFF),
    fieldBorder: Color(0xFF8E8AA3),
    onColor: Color(0xFFFFFFFF),
    onColorSoft: Color(0x40FFFFFF), // white 25 %
    onColorTrack: Color(0x24FFFFFF), // white 14 %
    splashOnColor: Color(0x1FFFFFFF), // white 12 %
    highlightOnColor: Color(0x0FFFFFFF), // white 6 %
    photoVeil: Color(0x4D000000), // black 30 %
    quoteWatermark: Color(0x1AFFFFFF), // white 10 %
    mediaBackdrop: Color(0xFF000000),
  );

  static const dark = KararColors(
    brightness: Brightness.dark,
    canvas: Color(0xFF0D0C12),
    surface: Color(0xFF17151F),
    elevated: Color(0xFF1F1C2A),
    muted: Color(0xFF262233),
    outlineVariant: Color(0xFF2F2B3D),
    outline: Color(0xFF77728F),
    text1: Color(0xFFF4F3F8),
    text2: Color(0xFFB6B2C6),
    text3: Color(0xFF958FAA),
    primary: Color(0xFFA98BFF),
    primaryFill: Color(0xFF7D50F0),
    primaryText: Color(0xFFA98BFF),
    primaryContainer: Color(0xFF2A2145),
    onPrimaryContainer: Color(0xFFDCD0FF),
    navActive: Color(0xFFB39AFF),
    // Brand violet instead of an inverse off-white block (glare).
    ink: Color(0xFF7D50F0),
    onInk: Color(0xFFFFFFFF),
    hakliFill: Color(0xFF15803D),
    hakliText: Color(0xFF4ADE80),
    // Vote fill over `surface`, matched per side like the verdict panel's
    // tonal pills: Haklı 26 %, Haksız 17 %. Near-equal OKLab lightness and
    // chroma (L .288 / .264, C .038 / .046), so the red never reads as a
    // maroon slab next to a muted green (a shared 30 % gave C .074 vs
    // .044). 1.29 / 1.16:1 against the surface; text tone on it 8.02 /
    // 6.18:1.
    hakliSoft: Color(0xFF163127),
    haksizFill: Color(0xFFD6363C),
    haksizText: Color(0xFFFF7A7A),
    haksizSoft: Color(0xFF371B24),
    gold: Color(0xFFF5C04E),
    goldText: Color(0xFFF5C04E),
    warnSoft: Color(0xFF2E2716),
    warnText: Color(0xFFF5C04E),
    mint: Color(0xFF57CA8C),
    scrim: Color(0x99000000), // rgba(0,0,0,.60)
    capsule: Color(0xFFF4F3F8), // spec off-white (not a dimmed grey)
    onCapsuleText: Color(0xFF15131F),
    onCapsuleMuted: Color(0xFF5B576D),
    onCapsuleHakli: Color(0xFF116B34),
    onCapsuleHaksiz: Color(0xFFB4232A),
    navPill: Color(0xF01F1C2A), // #1F1C2A @ 94 %
    sheet: Color(0xFF1F1C2A),
    hairline: Color(0x14FFFFFF), // white 8 %
    shimmerBase: Color(0xFF1F1C2A),
    shimmerHighlight: Color(0xFF2A2638),
    raised: Color(0xFF3A3552),
    fieldFill: Color(0xFF262233),
    fieldBorder: Color(0xFF706B8A),
    onColor: Color(0xFFFFFFFF),
    onColorSoft: Color(0x40FFFFFF), // white 25 %
    onColorTrack: Color(0x24FFFFFF), // white 14 %
    splashOnColor: Color(0x1FFFFFFF), // white 12 %
    highlightOnColor: Color(0x0FFFFFFF), // white 6 %
    photoVeil: Color(0x4D000000), // black 30 %
    quoteWatermark: Color(0x1AFFFFFF), // white 10 %
    mediaBackdrop: Color(0xFF000000),
  );

  @override
  KararColors copyWith({
    Brightness? brightness,
    Color? canvas,
    Color? surface,
    Color? elevated,
    Color? muted,
    Color? outlineVariant,
    Color? outline,
    Color? text1,
    Color? text2,
    Color? text3,
    Color? primary,
    Color? primaryFill,
    Color? primaryText,
    Color? primaryContainer,
    Color? onPrimaryContainer,
    Color? navActive,
    Color? ink,
    Color? onInk,
    Color? hakliFill,
    Color? hakliText,
    Color? hakliSoft,
    Color? haksizFill,
    Color? haksizText,
    Color? haksizSoft,
    Color? gold,
    Color? goldText,
    Color? warnSoft,
    Color? warnText,
    Color? mint,
    Color? scrim,
    Color? capsule,
    Color? onCapsuleText,
    Color? onCapsuleMuted,
    Color? onCapsuleHakli,
    Color? onCapsuleHaksiz,
    Color? navPill,
    Color? sheet,
    Color? hairline,
    Color? shimmerBase,
    Color? shimmerHighlight,
    Color? raised,
    Color? fieldFill,
    Color? fieldBorder,
    Color? onColor,
    Color? onColorSoft,
    Color? onColorTrack,
    Color? splashOnColor,
    Color? highlightOnColor,
    Color? photoVeil,
    Color? quoteWatermark,
    Color? mediaBackdrop,
  }) {
    return KararColors(
      brightness: brightness ?? this.brightness,
      canvas: canvas ?? this.canvas,
      surface: surface ?? this.surface,
      elevated: elevated ?? this.elevated,
      muted: muted ?? this.muted,
      outlineVariant: outlineVariant ?? this.outlineVariant,
      outline: outline ?? this.outline,
      text1: text1 ?? this.text1,
      text2: text2 ?? this.text2,
      text3: text3 ?? this.text3,
      primary: primary ?? this.primary,
      primaryFill: primaryFill ?? this.primaryFill,
      primaryText: primaryText ?? this.primaryText,
      primaryContainer: primaryContainer ?? this.primaryContainer,
      onPrimaryContainer: onPrimaryContainer ?? this.onPrimaryContainer,
      navActive: navActive ?? this.navActive,
      ink: ink ?? this.ink,
      onInk: onInk ?? this.onInk,
      hakliFill: hakliFill ?? this.hakliFill,
      hakliText: hakliText ?? this.hakliText,
      hakliSoft: hakliSoft ?? this.hakliSoft,
      haksizFill: haksizFill ?? this.haksizFill,
      haksizText: haksizText ?? this.haksizText,
      haksizSoft: haksizSoft ?? this.haksizSoft,
      gold: gold ?? this.gold,
      goldText: goldText ?? this.goldText,
      warnSoft: warnSoft ?? this.warnSoft,
      warnText: warnText ?? this.warnText,
      mint: mint ?? this.mint,
      scrim: scrim ?? this.scrim,
      capsule: capsule ?? this.capsule,
      onCapsuleText: onCapsuleText ?? this.onCapsuleText,
      onCapsuleMuted: onCapsuleMuted ?? this.onCapsuleMuted,
      onCapsuleHakli: onCapsuleHakli ?? this.onCapsuleHakli,
      onCapsuleHaksiz: onCapsuleHaksiz ?? this.onCapsuleHaksiz,
      navPill: navPill ?? this.navPill,
      sheet: sheet ?? this.sheet,
      hairline: hairline ?? this.hairline,
      shimmerBase: shimmerBase ?? this.shimmerBase,
      shimmerHighlight: shimmerHighlight ?? this.shimmerHighlight,
      raised: raised ?? this.raised,
      fieldFill: fieldFill ?? this.fieldFill,
      fieldBorder: fieldBorder ?? this.fieldBorder,
      onColor: onColor ?? this.onColor,
      onColorSoft: onColorSoft ?? this.onColorSoft,
      onColorTrack: onColorTrack ?? this.onColorTrack,
      splashOnColor: splashOnColor ?? this.splashOnColor,
      highlightOnColor: highlightOnColor ?? this.highlightOnColor,
      photoVeil: photoVeil ?? this.photoVeil,
      quoteWatermark: quoteWatermark ?? this.quoteWatermark,
      mediaBackdrop: mediaBackdrop ?? this.mediaBackdrop,
    );
  }

  @override
  KararColors lerp(ThemeExtension<KararColors>? other, double t) {
    if (other is! KararColors) return this;
    Color c(Color a, Color b) => Color.lerp(a, b, t)!;
    return KararColors(
      brightness: t < 0.5 ? brightness : other.brightness,
      canvas: c(canvas, other.canvas),
      surface: c(surface, other.surface),
      elevated: c(elevated, other.elevated),
      muted: c(muted, other.muted),
      outlineVariant: c(outlineVariant, other.outlineVariant),
      outline: c(outline, other.outline),
      text1: c(text1, other.text1),
      text2: c(text2, other.text2),
      text3: c(text3, other.text3),
      primary: c(primary, other.primary),
      primaryFill: c(primaryFill, other.primaryFill),
      primaryText: c(primaryText, other.primaryText),
      primaryContainer: c(primaryContainer, other.primaryContainer),
      onPrimaryContainer: c(onPrimaryContainer, other.onPrimaryContainer),
      navActive: c(navActive, other.navActive),
      ink: c(ink, other.ink),
      onInk: c(onInk, other.onInk),
      hakliFill: c(hakliFill, other.hakliFill),
      hakliText: c(hakliText, other.hakliText),
      hakliSoft: c(hakliSoft, other.hakliSoft),
      haksizFill: c(haksizFill, other.haksizFill),
      haksizText: c(haksizText, other.haksizText),
      haksizSoft: c(haksizSoft, other.haksizSoft),
      gold: c(gold, other.gold),
      goldText: c(goldText, other.goldText),
      warnSoft: c(warnSoft, other.warnSoft),
      warnText: c(warnText, other.warnText),
      mint: c(mint, other.mint),
      scrim: c(scrim, other.scrim),
      capsule: c(capsule, other.capsule),
      onCapsuleText: c(onCapsuleText, other.onCapsuleText),
      onCapsuleMuted: c(onCapsuleMuted, other.onCapsuleMuted),
      onCapsuleHakli: c(onCapsuleHakli, other.onCapsuleHakli),
      onCapsuleHaksiz: c(onCapsuleHaksiz, other.onCapsuleHaksiz),
      navPill: c(navPill, other.navPill),
      sheet: c(sheet, other.sheet),
      hairline: c(hairline, other.hairline),
      shimmerBase: c(shimmerBase, other.shimmerBase),
      shimmerHighlight: c(shimmerHighlight, other.shimmerHighlight),
      raised: c(raised, other.raised),
      fieldFill: c(fieldFill, other.fieldFill),
      fieldBorder: c(fieldBorder, other.fieldBorder),
      onColor: c(onColor, other.onColor),
      onColorSoft: c(onColorSoft, other.onColorSoft),
      onColorTrack: c(onColorTrack, other.onColorTrack),
      splashOnColor: c(splashOnColor, other.splashOnColor),
      highlightOnColor: c(highlightOnColor, other.highlightOnColor),
      photoVeil: c(photoVeil, other.photoVeil),
      quoteWatermark: c(quoteWatermark, other.quoteWatermark),
      mediaBackdrop: c(mediaBackdrop, other.mediaBackdrop),
    );
  }
}

/// Fixed palettes that are the same in both themes.
abstract final class KararPalette {
  /// Avatar disc colours, in the original hue order (indigo, violet, pink,
  /// rose → plum, amber, emerald → ink, cyan, blue) so a user keeps the
  /// same slot. White initial: 6.29 / 5.70 / 6.04 / 7.02 / 5.02 / 8.81 /
  /// 5.36 / 5.17 : 1. No pure green or red (vote semantics).
  static const List<Color> avatar = [
    Color(0xFF4F46E5), // indigo
    Color(0xFF7C3AED), // violet
    Color(0xFFBE185D), // pink
    Color(0xFF9D2A6E), // plum (was rose: read as Haksız)
    Color(0xFFB45309), // amber
    Color(0xFF4A4766), // ink (was emerald: read as Haklı)
    Color(0xFF0E7490), // cyan
    Color(0xFF2563EB), // blue
  ];
}

/// `context.kc` / `context.isDark` shortcuts.
extension KararThemeX on BuildContext {
  /// Karar colour tokens of the current theme. Falls back to the light/dark
  /// set by brightness when a test or a bare `MaterialApp` did not install
  /// the extension.
  KararColors get kc {
    final theme = Theme.of(this);
    return theme.extension<KararColors>() ??
        (theme.brightness == Brightness.dark
            ? KararColors.dark
            : KararColors.light);
  }

  bool get isDark => Theme.of(this).brightness == Brightness.dark;
}

/// Spacing scale (§2.5).
abstract final class KararSpace {
  static const double s2 = 2;
  static const double s4 = 4;
  static const double s6 = 6;
  static const double s8 = 8;
  static const double s10 = 10;
  static const double s12 = 12;
  static const double s16 = 16;
  static const double s20 = 20;
  static const double s24 = 24;
  static const double s28 = 28;
  static const double s32 = 32;
  static const double s40 = 40;
  static const double s48 = 48;
  static const double s64 = 64;

  /// Mobile page gutter.
  static const double gutter = 16;

  /// Inner padding of post cards.
  static const double cardPadding = 20;

  /// Gap between feed cards.
  static const double feedGap = 20;

  /// Gap between page sections.
  static const double sectionGap = 28;

  /// Vertical padding of list rows.
  static const double rowPadding = 12;

  /// Minimum tap target (prefer 48 where space allows).
  static const double minTap = 44;
}

/// Corner radii (§2.6).
abstract final class KararRadius {
  /// OP chip.
  static const double xs = 6;

  /// Rail divider, small tiles, tooltips.
  static const double sm = 10;

  /// Rounded-square avatars.
  static const double avatar = 12;

  /// Notification icon tiles.
  static const double iconTile = 14;

  /// Inputs, menus, snackbars, small cards.
  static const double input = 16;

  /// Nav active indicator, side-nav items, popup menus.
  static const double navItem = 18;

  /// Grid tiles, list cards, comment highlight.
  static const double tile = 20;

  /// Surface cards, hero, AI card, settings groups.
  static const double surface = 24;

  /// Verdict panel, dialogs, nav pill.
  static const double panel = 28;

  /// Post cards and bottom sheets (reference).
  static const double card = 30;

  /// Pills and circles.
  static const double pill = 999;

  static BorderRadius all(double r) => BorderRadius.all(Radius.circular(r));
}

/// Shadow kinds a [KararSurface]-style container can use.
enum KararShadowKind { none, soft, float, capsule }

/// Shadows / elevation (§2.7). Light theme uses soft violet-black shadows,
/// dark theme uses hairlines instead (see [hairlineFor]).
abstract final class KararShadow {
  static const _ink = Color(0xFF141028); // rgb(20,16,40)

  /// Surface cards on canvas.
  static List<BoxShadow> soft(Brightness b) => b == Brightness.dark
      ? const []
      : [
          BoxShadow(
            color: _ink.withValues(alpha: 0.08),
            offset: const Offset(0, 8),
            blurRadius: 24,
          ),
          BoxShadow(
            color: _ink.withValues(alpha: 0.04),
            offset: const Offset(0, 2),
            blurRadius: 6,
          ),
        ];

  /// Nav pill, FAB, notch, popovers.
  static List<BoxShadow> float(Brightness b) => b == Brightness.dark
      ? [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.50),
            offset: const Offset(0, 16),
            blurRadius: 40,
          ),
        ]
      : [
          BoxShadow(
            color: _ink.withValues(alpha: 0.16),
            offset: const Offset(0, 14),
            blurRadius: 34,
          ),
          BoxShadow(
            color: _ink.withValues(alpha: 0.06),
            offset: const Offset(0, 2),
            blurRadius: 8,
          ),
        ];

  /// Gradient post cards: coloured glow from the palette (light only).
  static List<BoxShadow> card(Color glow, Brightness b) =>
      b == Brightness.dark
          ? const []
          : [
              BoxShadow(
                color: glow,
                offset: const Offset(0, 18),
                blurRadius: 36,
                spreadRadius: -14,
              ),
            ];

  /// Photo post cards (light only).
  static List<BoxShadow> photoCard(Brightness b) => b == Brightness.dark
      ? const []
      : [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.28),
            offset: const Offset(0, 16),
            blurRadius: 30,
            spreadRadius: -12,
          ),
        ];

  /// Rail buttons and verdict chip (both themes).
  static const List<BoxShadow> capsule = [
    BoxShadow(
      color: Color(0x29000000), // black 16 %
      offset: Offset(0, 6),
      blurRadius: 16,
    ),
  ];

  /// Selected vote capsule.
  static const List<BoxShadow> capsuleSelected = [
    BoxShadow(
      color: Color(0x40000000), // black 25 %
      offset: Offset(0, 8),
      blurRadius: 18,
    ),
  ];

  /// Brand CTA glow (light only).
  static List<BoxShadow> primaryButton(Brightness b) => b == Brightness.dark
      ? const []
      : [
          BoxShadow(
            color: const Color(0xFF7D50F0).withValues(alpha: 0.60),
            offset: const Offset(0, 10),
            blurRadius: 22,
            spreadRadius: -8,
          ),
        ];

  /// Logo mark glow (light only).
  static List<BoxShadow> logo(Brightness b) => b == Brightness.dark
      ? const []
      : [
          BoxShadow(
            color: const Color(0xFF7D50F0).withValues(alpha: 0.35),
            offset: const Offset(0, 8),
            blurRadius: 18,
          ),
        ];

  /// Shadow list for a [KararShadowKind].
  static List<BoxShadow> of(KararShadowKind kind, Brightness b) =>
      switch (kind) {
        KararShadowKind.none => const [],
        KararShadowKind.soft => soft(b),
        KararShadowKind.float => float(b),
        KararShadowKind.capsule => capsule,
      };

  /// Dark-mode hairline that replaces a shadow (null in light / for none).
  /// soft → white 8 %, float → white 9 %, cards → white 10 %.
  static Color? hairlineFor(KararShadowKind kind, Brightness b) {
    if (b != Brightness.dark) return null;
    return switch (kind) {
      KararShadowKind.none => null,
      KararShadowKind.soft => const Color(0x14FFFFFF),
      KararShadowKind.float => const Color(0x17FFFFFF),
      KararShadowKind.capsule => null,
    };
  }

  /// Inner hairline on post cards in dark mode (white 10 %).
  static const Color cardHairlineDark = Color(0x1AFFFFFF);

  /// Stronger dark-mode card hairline (white 13 %) for low-key palettes
  /// (Diğer / ink) and "seen" cards, whose bottom stop sits close to the
  /// canvas.
  static const Color cardHairlineDarkStrong = Color(0x21FFFFFF);

  /// Raised indicator on a muted track (segmented control): light soft
  /// lift, none in dark (the dark indicator uses a hairline instead).
  static List<BoxShadow> raised(Brightness b) => b == Brightness.dark
      ? const []
      : [
          BoxShadow(
            color: _ink.withValues(alpha: 0.10),
            offset: const Offset(0, 2),
            blurRadius: 8,
          ),
          BoxShadow(
            color: _ink.withValues(alpha: 0.04),
            offset: const Offset(0, 1),
            blurRadius: 2,
          ),
        ];
}

/// Glass parameters (§2.8).
abstract final class KararGlass {
  /// Rail fill on gradient cards (baked glass, no blur).
  static const Color railFill = Color(0x29FFFFFF); // white 16 %

  /// Rail fill on photos (live blur).
  static const Color railFillPhoto = Color(0x2EFFFFFF); // white 18 %

  /// High-contrast rail fill on photos.
  static const Color railFillHighContrast = Color(0x73000000); // black 45 %

  /// Sheen gradient top colour (fades to [sheenBottom]).
  static const Color sheenTop = Color(0x1AFFFFFF); // white 10 %
  static const Color sheenBottom = Color(0x00FFFFFF);

  /// Secondary rail circles (Kaydet / Paylaş) are glass, not solid
  /// capsules (reference): black 22 % on gradients, 28 % on photos, with a
  /// 1 px [stroke]; white icon ≥ 5.6:1 on every palette.
  static const Color railCircle = Color(0x38000000); // black 22 %
  static const Color railCirclePhoto = Color(0x47000000); // black 28 %

  /// Extra veil on "seen" photo cards (black 20 %).
  static const Color seenPhotoVeil = Color(0x33000000);

  /// 1 px stroke around the rail.
  static const Color stroke = Color(0x47FFFFFF); // white 28 %

  /// Live blur sigma for the rail on photos.
  static const double railBlur = 14;

  /// `OnColorChip` fills.
  static const Color chipOnColor = Color(0x38000000); // black 22 %
  static const Color chipOnPhoto = Color(0x47000000); // black 28 %
  static const Color chipHighContrast = Color(0xFF15131F);
  static const double chipBlur = 10;

  /// Dark nav pill blur.
  static const double navBlur = 20;

  /// Scrolled top bar: canvas @ 88 % + blur.
  static const double barAlpha = 0.88;
  static const double barBlur = 16;

  /// Rail divider (22 × 2) colour.
  static const Color railDivider = Color(0x8CFFFFFF); // white 55 %
}

/// Brand and surface gradients that are not tied to a category
/// (category palettes live in `CategoryStyle`).
abstract final class KararGradients {
  /// Logo mark (145°).
  static const LinearGradient logo = LinearGradient(
    begin: Alignment(-0.57, -0.82),
    end: Alignment(0.57, 0.82),
    colors: [Color(0xFF8E66F7), Color(0xFF6A3FE6)],
  );

  /// Text-post decor: white 18 % radial sheen at the top-left.
  static const RadialGradient cardSheen = RadialGradient(
    center: Alignment.topLeft,
    radius: 0.9,
    colors: [Color(0x2EFFFFFF), Color(0x00FFFFFF)],
    stops: [0, 0.6],
  );

  /// Text-post decor: white 10 % radial glow behind the rail (right-middle).
  static const RadialGradient cardSheenRail = RadialGradient(
    center: Alignment.centerRight,
    radius: 0.6,
    colors: [Color(0x1AFFFFFF), Color(0x00FFFFFF)],
    stops: [0, 0.7],
  );

  /// Dark theme: the same sheens, a touch softer so dark cards keep depth
  /// without a milky top-left.
  static const RadialGradient cardSheenDark = RadialGradient(
    center: Alignment.topLeft,
    radius: 0.9,
    colors: [Color(0x24FFFFFF), Color(0x00FFFFFF)],
    stops: [0, 0.6],
  );

  /// "Bu Haftanın Kararı" hero and ink digest cards (both themes).
  static const RadialGradient hero = RadialGradient(
    center: Alignment.topLeft,
    radius: 1.4,
    colors: [Color(0xFF3A2F5E), Color(0xFF1B1828)],
  );

  /// Dark weekly hero: on the near-black canvas the plain [hero] read as one
  /// more grey card, so dark gets a brighter violet core — the brand fill
  /// blended at 28 % into the hero's top stop (#4D3887) — before the regular
  /// [hero] stops (white text ≥ 9:1, gold overline ≥ 5.5:1 on it).
  static const RadialGradient heroDark = RadialGradient(
    center: Alignment.topLeft,
    radius: 1.4,
    colors: [Color(0xFF4D3887), Color(0xFF3A2F5E), Color(0xFF1B1828)],
    stops: [0, 0.45, 1],
  );

  /// Auth brand panel / violet fallback.
  static const LinearGradient brand = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF7D50F0), Color(0xFF3F1A9E)],
  );

  /// Profile avatar ring (sweep).
  static const SweepGradient avatarRing = SweepGradient(
    colors: [
      Color(0xFF7D50F0),
      Color(0xFFC2307E),
      Color(0xFFF5C04E),
      Color(0xFF7D50F0),
    ],
  );

  /// Streak pill.
  static const LinearGradient streak = LinearGradient(
    colors: [Color(0xFFF5C04E), Color(0xFFE8873A)],
  );

  /// Photo card scrims (§2.4): top 38 % → 0 over the top 26 %, bottom
  /// 0 at 30 % → 52 % at 52 % → 80 % at 100 %.
  static const LinearGradient photoScrim = LinearGradient(
    begin: Alignment.topCenter,
    end: Alignment.bottomCenter,
    colors: [
      Color(0x61000000),
      Color(0x00000000),
      Color(0x00000000),
      Color(0x85000000),
      Color(0xCC000000),
    ],
    stops: [0, 0.26, 0.30, 0.52, 1],
  );

  /// Vote fills for split bars (same in both themes).
  static const Color hakli = Color(0xFF15803D);
  static const Color haksiz = Color(0xFFD6363C);

  /// Mini bars on the ink hero (dark background).
  static const Color hakliOnInk = Color(0xFF4ADE80);
  static const Color haksizOnInk = Color(0xFFFF7A7A);

  /// Confetti colours.
  static const List<Color> confetti = [
    Color(0xFF15803D),
    Color(0xFFD6363C),
    Color(0xFF7D50F0),
    Color(0xFFF5C04E),
  ];
}
