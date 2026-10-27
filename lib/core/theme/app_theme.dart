import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'app_typography.dart';
import 'karar_tokens.dart';

/// Karar ThemeData (spec §3). Light and dark share one builder; every colour
/// comes from [KararColors] so `Theme.of(context)` and `context.kc` agree.
///
/// Rules baked in here:
/// * Material elevation is 0 everywhere and `surfaceTintColor` is
///   transparent (depth comes from `KararShadow` / dark hairlines).
/// * `colorScheme.primary` is lavender (#A98BFF) in dark: it is only ever
///   paired with `onPrimary`. White-on-brand uses `kc.primaryFill`.
/// * Visual density is standard (no compact densities: they shrink tap
///   targets) and tap targets are padded to 48 on every platform.
abstract final class AppTheme {
  static ThemeData light() => _build(KararColors.light, _lightScheme);

  static ThemeData dark() => _build(KararColors.dark, _darkScheme);

  static const ColorScheme _lightScheme = ColorScheme(
    brightness: Brightness.light,
    primary: Color(0xFF7D50F0),
    onPrimary: Color(0xFFFFFFFF),
    primaryContainer: Color(0xFFEEE8FD),
    onPrimaryContainer: Color(0xFF4A22B5),
    secondary: Color(0xFF6438E0),
    onSecondary: Color(0xFFFFFFFF),
    secondaryContainer: Color(0xFFEEE8FD),
    onSecondaryContainer: Color(0xFF4A22B5),
    tertiary: Color(0xFFF5C04E),
    onTertiary: Color(0xFF15131F),
    tertiaryContainer: Color(0xFFFFF4D6),
    onTertiaryContainer: Color(0xFF8A5A00),
    error: Color(0xFFD6363C),
    onError: Color(0xFFFFFFFF),
    errorContainer: Color(0xFFFDE8E8),
    onErrorContainer: Color(0xFF8A1C21),
    surface: Color(0xFFFFFFFF),
    onSurface: Color(0xFF15131F),
    onSurfaceVariant: Color(0xFF5B576D),
    surfaceDim: Color(0xFFEFEDF5),
    surfaceBright: Color(0xFFFFFFFF),
    surfaceContainerLowest: Color(0xFFFFFFFF),
    surfaceContainerLow: Color(0xFFF9F8FC),
    surfaceContainer: Color(0xFFF5F4F9),
    surfaceContainerHigh: Color(0xFFF1EFF7),
    surfaceContainerHighest: Color(0xFFEFEDF5),
    outline: Color(0xFF8E8AA3),
    outlineVariant: Color(0xFFE7E5EE),
    shadow: Color(0xFF141028),
    scrim: Color(0xFF0A0814),
    inverseSurface: Color(0xFF1F1C2A),
    onInverseSurface: Color(0xFFF4F3F8),
    inversePrimary: Color(0xFFB39AFF),
    surfaceTint: Colors.transparent,
  );

  static const ColorScheme _darkScheme = ColorScheme(
    brightness: Brightness.dark,
    primary: Color(0xFFA98BFF),
    onPrimary: Color(0xFF15131F),
    primaryContainer: Color(0xFF2A2145),
    onPrimaryContainer: Color(0xFFDCD0FF),
    secondary: Color(0xFFB39AFF),
    onSecondary: Color(0xFF15131F),
    secondaryContainer: Color(0xFF2A2145),
    onSecondaryContainer: Color(0xFFDCD0FF),
    tertiary: Color(0xFFF5C04E),
    onTertiary: Color(0xFF15131F),
    tertiaryContainer: Color(0xFF2E2716),
    onTertiaryContainer: Color(0xFFF5C04E),
    error: Color(0xFFFF7A7A),
    onError: Color(0xFF2B0A0C),
    errorContainer: Color(0xFF3A1518),
    onErrorContainer: Color(0xFFFFD9D9),
    surface: Color(0xFF17151F),
    onSurface: Color(0xFFF4F3F8),
    onSurfaceVariant: Color(0xFFB6B2C6),
    surfaceDim: Color(0xFF0D0C12),
    surfaceBright: Color(0xFF262233),
    surfaceContainerLowest: Color(0xFF0D0C12),
    surfaceContainerLow: Color(0xFF131119),
    surfaceContainer: Color(0xFF17151F),
    surfaceContainerHigh: Color(0xFF1F1C2A),
    surfaceContainerHighest: Color(0xFF262233),
    outline: Color(0xFF77728F),
    outlineVariant: Color(0xFF2F2B3D),
    shadow: Color(0xFF000000),
    scrim: Color(0xFF000000),
    inverseSurface: Color(0xFFF4F3F8),
    onInverseSurface: Color(0xFF15131F),
    inversePrimary: Color(0xFF6438E0),
    surfaceTint: Colors.transparent,
  );

  static ThemeData _build(KararColors k, ColorScheme cs) {
    final isDark = k.isDark;
    final brightness = isDark ? Brightness.dark : Brightness.light;
    const stadium = StadiumBorder();
    final textTheme = AppTypography.textTheme.apply(
      bodyColor: k.text1,
      displayColor: k.text1,
    );

    WidgetStateProperty<Color?> overlay({
      required Color base,
      double pressed = 0.08,
      double hovered = 0.04,
      double focused = 0.08,
    }) {
      return WidgetStateProperty.resolveWith((states) {
        if (states.contains(WidgetState.pressed)) {
          return base.withValues(alpha: pressed);
        }
        if (states.contains(WidgetState.focused)) {
          return base.withValues(alpha: focused);
        }
        if (states.contains(WidgetState.hovered)) {
          return base.withValues(alpha: hovered);
        }
        return null;
      });
    }

    OutlineInputBorder inputBorder(Color color, double width) =>
        OutlineInputBorder(
          borderRadius: KararRadius.all(KararRadius.input),
          borderSide: BorderSide(color: color, width: width),
        );

    final darkHairline = BorderSide(color: k.hairline);

    return ThemeData(
      useMaterial3: true,
      brightness: brightness,
      colorScheme: cs,
      fontFamily: AppTypography.fontFamily,
      textTheme: textTheme,
      primaryTextTheme: AppTypography.textTheme,
      extensions: [k],
      scaffoldBackgroundColor: k.canvas,
      canvasColor: k.surface,
      cardColor: k.surface,
      dividerColor: k.outlineVariant,
      disabledColor: k.text3.withValues(alpha: 0.6),
      hintColor: k.text3,
      shadowColor: cs.shadow,
      focusColor: k.primary.withValues(alpha: 0.12),
      hoverColor: k.text1.withValues(alpha: 0.04),
      highlightColor: k.text1.withValues(alpha: 0.06),
      splashColor: k.text1.withValues(alpha: 0.08),
      visualDensity: VisualDensity.standard,
      materialTapTargetSize: MaterialTapTargetSize.padded,
      iconTheme: IconThemeData(color: k.text1, size: 24),
      primaryIconTheme: const IconThemeData(color: Colors.white, size: 24),

      // -- App bar ----------------------------------------------------------
      appBarTheme: AppBarThemeData(
        backgroundColor: k.canvas,
        foregroundColor: k.text1,
        elevation: 0,
        scrolledUnderElevation: 0,
        surfaceTintColor: Colors.transparent,
        shadowColor: Colors.transparent,
        centerTitle: true,
        toolbarHeight: 64,
        titleTextStyle: AppTypography.appBarTitle.copyWith(color: k.text1),
        toolbarTextStyle: AppTypography.bodyStrong.copyWith(color: k.text1),
        iconTheme: IconThemeData(color: k.text1, size: 24),
        actionsIconTheme: IconThemeData(color: k.text1, size: 24),
        actionsPadding: const EdgeInsets.only(right: 8),
        systemOverlayStyle: (isDark
                ? SystemUiOverlayStyle.light
                : SystemUiOverlayStyle.dark)
            .copyWith(statusBarColor: Colors.transparent),
      ),
      actionIconTheme: ActionIconThemeData(
        backButtonIconBuilder: (context) =>
            const Icon(Icons.arrow_back_ios_new_rounded, size: 20),
        closeButtonIconBuilder: (context) =>
            const Icon(Icons.close_rounded, size: 22),
      ),

      // -- Navigation -------------------------------------------------------
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: k.navPill,
        surfaceTintColor: Colors.transparent,
        shadowColor: Colors.transparent,
        elevation: 0,
        height: 70,
        indicatorColor: k.primaryContainer,
        indicatorShape: RoundedRectangleBorder(
          borderRadius: KararRadius.all(KararRadius.navItem),
        ),
        labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
        iconTheme: WidgetStateProperty.resolveWith(
          (states) => IconThemeData(
            size: 24,
            color: states.contains(WidgetState.selected)
                ? k.navActive
                : k.text2,
          ),
        ),
        labelTextStyle: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected)
              ? AppTypography.meta.copyWith(
                  fontWeight: FontWeight.w800,
                  color: k.primaryText,
                  letterSpacing: 0,
                )
              : AppTypography.meta.copyWith(
                  fontWeight: FontWeight.w700,
                  color: k.text2,
                  letterSpacing: 0,
                ),
        ),
        overlayColor: overlay(base: k.text1),
      ),
      navigationRailTheme: NavigationRailThemeData(
        backgroundColor: k.canvas,
        elevation: 0,
        minWidth: 80,
        minExtendedWidth: 248,
        useIndicator: true,
        indicatorColor: k.primaryContainer,
        indicatorShape: RoundedRectangleBorder(
          borderRadius: KararRadius.all(KararRadius.navItem),
        ),
        selectedIconTheme: IconThemeData(color: k.navActive, size: 24),
        unselectedIconTheme: IconThemeData(color: k.text2, size: 24),
        selectedLabelTextStyle: AppTypography.bodyStrong.copyWith(
          fontWeight: FontWeight.w800,
          color: k.primaryText,
        ),
        unselectedLabelTextStyle: AppTypography.bodyStrong.copyWith(
          fontWeight: FontWeight.w600,
          color: k.text2,
        ),
      ),
      bottomNavigationBarTheme: BottomNavigationBarThemeData(
        backgroundColor: k.navPill,
        elevation: 0,
        type: BottomNavigationBarType.fixed,
        selectedItemColor: k.primaryText,
        unselectedItemColor: k.text2,
        selectedLabelStyle:
            AppTypography.meta.copyWith(fontWeight: FontWeight.w800),
        unselectedLabelStyle:
            AppTypography.meta.copyWith(fontWeight: FontWeight.w700),
      ),
      drawerTheme: DrawerThemeData(
        backgroundColor: k.sheet,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrimColor: k.scrim,
      ),

      // -- Surfaces ---------------------------------------------------------
      cardTheme: CardThemeData(
        color: k.surface,
        elevation: 0,
        shadowColor: Colors.transparent,
        surfaceTintColor: Colors.transparent,
        margin: EdgeInsets.zero,
        clipBehavior: Clip.antiAlias,
        shape: RoundedRectangleBorder(
          borderRadius: KararRadius.all(KararRadius.surface),
          side: isDark ? darkHairline : BorderSide.none,
        ),
      ),
      bottomSheetTheme: BottomSheetThemeData(
        backgroundColor: k.sheet,
        modalBackgroundColor: k.sheet,
        surfaceTintColor: Colors.transparent,
        shadowColor: Colors.transparent,
        elevation: 0,
        modalElevation: 0,
        shape: const RoundedRectangleBorder(
          borderRadius:
              BorderRadius.vertical(top: Radius.circular(KararRadius.card)),
        ),
        showDragHandle: false,
        dragHandleColor: k.outlineVariant,
        dragHandleSize: const Size(40, 5),
        modalBarrierColor: k.scrim,
        clipBehavior: Clip.antiAlias,
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: k.sheet,
        surfaceTintColor: Colors.transparent,
        shadowColor: Colors.transparent,
        elevation: 0,
        barrierColor: k.scrim,
        shape: RoundedRectangleBorder(
          borderRadius: KararRadius.all(KararRadius.panel),
          side: isDark ? darkHairline : BorderSide.none,
        ),
        titleTextStyle: AppTypography.headline.copyWith(color: k.text1),
        contentTextStyle: AppTypography.body.copyWith(color: k.text2),
        actionsPadding: const EdgeInsets.fromLTRB(24, 0, 24, 20),
      ),
      popupMenuTheme: PopupMenuThemeData(
        color: k.elevated,
        surfaceTintColor: Colors.transparent,
        elevation: 8,
        shadowColor: isDark
            ? Colors.black.withValues(alpha: 0.6)
            : const Color(0xFF141028).withValues(alpha: 0.35),
        shape: RoundedRectangleBorder(
          borderRadius: KararRadius.all(KararRadius.navItem),
          side: isDark ? darkHairline : BorderSide.none,
        ),
        textStyle: AppTypography.bodyStrong.copyWith(
          fontWeight: FontWeight.w600,
          color: k.text1,
        ),
        labelTextStyle: WidgetStatePropertyAll(
          AppTypography.bodyStrong.copyWith(
            fontWeight: FontWeight.w600,
            color: k.text1,
          ),
        ),
        iconColor: k.text2,
        iconSize: 22,
      ),
      menuTheme: MenuThemeData(
        style: MenuStyle(
          backgroundColor: WidgetStatePropertyAll(k.elevated),
          surfaceTintColor: const WidgetStatePropertyAll(Colors.transparent),
          elevation: const WidgetStatePropertyAll(8),
          shape: WidgetStatePropertyAll(
            RoundedRectangleBorder(
              borderRadius: KararRadius.all(KararRadius.input),
              side: isDark ? darkHairline : BorderSide.none,
            ),
          ),
        ),
      ),
      snackBarTheme: SnackBarThemeData(
        behavior: SnackBarBehavior.floating,
        backgroundColor: cs.inverseSurface,
        contentTextStyle: AppTypography.bodyS.copyWith(
          fontWeight: FontWeight.w600,
          color: cs.onInverseSurface,
        ),
        actionTextColor: cs.inversePrimary,
        closeIconColor: cs.onInverseSurface,
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: KararRadius.all(KararRadius.input),
        ),
        insetPadding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
      ),
      tooltipTheme: TooltipThemeData(
        decoration: BoxDecoration(
          color: cs.inverseSurface,
          borderRadius: KararRadius.all(KararRadius.sm),
        ),
        textStyle: AppTypography.labelS.copyWith(
          fontWeight: FontWeight.w600,
          color: cs.onInverseSurface,
        ),
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
        waitDuration: const Duration(milliseconds: 400),
      ),
      bannerTheme: MaterialBannerThemeData(
        backgroundColor: k.warnSoft,
        surfaceTintColor: Colors.transparent,
        contentTextStyle: AppTypography.bodyS.copyWith(color: k.warnText),
        elevation: 0,
        dividerColor: Colors.transparent,
      ),
      dividerTheme: DividerThemeData(
        color: k.outlineVariant,
        thickness: 1,
        space: 1,
      ),
      listTileTheme: ListTileThemeData(
        minTileHeight: 56,
        contentPadding: const EdgeInsets.symmetric(horizontal: 16),
        iconColor: k.text2,
        textColor: k.text1,
        titleTextStyle: AppTypography.bodyStrong.copyWith(color: k.text1),
        subtitleTextStyle: AppTypography.bodyS.copyWith(color: k.text2),
        leadingAndTrailingTextStyle:
            AppTypography.labelS.copyWith(color: k.text2),
        selectedColor: k.primaryText,
        selectedTileColor: k.primaryContainer,
        shape: RoundedRectangleBorder(
          borderRadius: KararRadius.all(KararRadius.input),
        ),
      ),
      expansionTileTheme: ExpansionTileThemeData(
        iconColor: k.text2,
        collapsedIconColor: k.text2,
        textColor: k.text1,
        collapsedTextColor: k.text1,
        shape: const Border(),
        collapsedShape: const Border(),
      ),
      badgeTheme: BadgeThemeData(
        backgroundColor: k.haksizFill,
        textColor: Colors.white,
        smallSize: 8,
        largeSize: 18,
        padding: const EdgeInsets.symmetric(horizontal: 5),
        textStyle: AppTypography.micro.copyWith(
          color: Colors.white,
          letterSpacing: 0,
        ),
      ),

      // -- Buttons ----------------------------------------------------------
      filledButtonTheme: FilledButtonThemeData(
        style: ButtonStyle(
          backgroundColor: WidgetStateProperty.resolveWith(
            (states) =>
                states.contains(WidgetState.disabled) ? k.muted : k.primaryFill,
          ),
          foregroundColor: WidgetStateProperty.resolveWith(
            (states) =>
                states.contains(WidgetState.disabled) ? k.text3 : Colors.white,
          ),
          iconColor: WidgetStateProperty.resolveWith(
            (states) =>
                states.contains(WidgetState.disabled) ? k.text3 : Colors.white,
          ),
          overlayColor: overlay(base: Colors.black),
          elevation: const WidgetStatePropertyAll(0),
          shadowColor: const WidgetStatePropertyAll(Colors.transparent),
          surfaceTintColor: const WidgetStatePropertyAll(Colors.transparent),
          minimumSize: const WidgetStatePropertyAll(Size(64, 52)),
          padding: const WidgetStatePropertyAll(
            EdgeInsets.symmetric(horizontal: 24),
          ),
          shape: const WidgetStatePropertyAll(stadium),
          textStyle: const WidgetStatePropertyAll(AppTypography.buttonL),
          iconSize: const WidgetStatePropertyAll(20),
        ),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: k.surface,
          foregroundColor: k.text1,
          disabledBackgroundColor: k.muted,
          disabledForegroundColor: k.text3,
          elevation: 0,
          shadowColor: Colors.transparent,
          surfaceTintColor: Colors.transparent,
          minimumSize: const Size(64, 52),
          padding: const EdgeInsets.symmetric(horizontal: 24),
          shape: stadium,
          textStyle: AppTypography.buttonL,
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: ButtonStyle(
          foregroundColor: WidgetStateProperty.resolveWith(
            (states) =>
                states.contains(WidgetState.disabled) ? k.text3 : k.text1,
          ),
          iconColor: WidgetStateProperty.resolveWith(
            (states) =>
                states.contains(WidgetState.disabled) ? k.text3 : k.text1,
          ),
          overlayColor: overlay(base: k.text1),
          side: WidgetStateProperty.resolveWith(
            (states) => BorderSide(
              color: states.contains(WidgetState.disabled)
                  ? k.outlineVariant
                  : states.contains(WidgetState.focused)
                      ? k.primary
                      : k.outline,
              width: states.contains(WidgetState.focused) ? 2 : 1.5,
            ),
          ),
          minimumSize: const WidgetStatePropertyAll(Size(64, 52)),
          padding: const WidgetStatePropertyAll(
            EdgeInsets.symmetric(horizontal: 24),
          ),
          shape: const WidgetStatePropertyAll(stadium),
          textStyle: const WidgetStatePropertyAll(AppTypography.buttonL),
          iconSize: const WidgetStatePropertyAll(20),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: ButtonStyle(
          foregroundColor: WidgetStateProperty.resolveWith(
            (states) => states.contains(WidgetState.disabled)
                ? k.text3
                : k.primaryText,
          ),
          iconColor: WidgetStateProperty.resolveWith(
            (states) => states.contains(WidgetState.disabled)
                ? k.text3
                : k.primaryText,
          ),
          overlayColor: overlay(base: k.primary),
          minimumSize: const WidgetStatePropertyAll(Size(44, 44)),
          padding: const WidgetStatePropertyAll(
            EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          ),
          shape: const WidgetStatePropertyAll(stadium),
          textStyle: WidgetStatePropertyAll(
            AppTypography.labelS.copyWith(fontSize: 15, height: 20 / 15),
          ),
          iconSize: const WidgetStatePropertyAll(18),
        ),
      ),
      iconButtonTheme: IconButtonThemeData(
        style: ButtonStyle(
          minimumSize: const WidgetStatePropertyAll(Size(44, 44)),
          visualDensity: VisualDensity.standard,
          overlayColor: overlay(base: k.text1),
        ),
      ),
      floatingActionButtonTheme: FloatingActionButtonThemeData(
        backgroundColor: k.primaryFill,
        foregroundColor: Colors.white,
        elevation: 0,
        focusElevation: 0,
        hoverElevation: 0,
        highlightElevation: 0,
        disabledElevation: 0,
        shape: const CircleBorder(),
        extendedTextStyle: AppTypography.label,
      ),
      segmentedButtonTheme: SegmentedButtonThemeData(
        style: ButtonStyle(
          backgroundColor: WidgetStateProperty.resolveWith(
            (states) =>
                states.contains(WidgetState.selected) ? k.ink : k.muted,
          ),
          foregroundColor: WidgetStateProperty.resolveWith(
            (states) =>
                states.contains(WidgetState.selected) ? k.onInk : k.text2,
          ),
          iconColor: WidgetStateProperty.resolveWith(
            (states) =>
                states.contains(WidgetState.selected) ? k.onInk : k.text2,
          ),
          side: const WidgetStatePropertyAll(BorderSide.none),
          shape: const WidgetStatePropertyAll(stadium),
          textStyle: const WidgetStatePropertyAll(AppTypography.labelS),
          minimumSize: const WidgetStatePropertyAll(Size(44, 44)),
        ),
      ),
      toggleButtonsTheme: ToggleButtonsThemeData(
        color: k.text2,
        selectedColor: k.onInk,
        fillColor: k.ink,
        borderColor: Colors.transparent,
        selectedBorderColor: Colors.transparent,
        borderRadius: KararRadius.all(KararRadius.pill),
        constraints: const BoxConstraints(minWidth: 44, minHeight: 44),
      ),

      // -- Inputs -----------------------------------------------------------
      inputDecorationTheme: InputDecorationThemeData(
        filled: true,
        fillColor: k.muted,
        isDense: false,
        contentPadding: const EdgeInsets.all(16),
        border: inputBorder(k.outlineVariant, 1),
        enabledBorder: inputBorder(k.outlineVariant, 1),
        focusedBorder: inputBorder(k.primary, 2),
        errorBorder: inputBorder(cs.error, 1.5),
        focusedErrorBorder: inputBorder(cs.error, 2),
        disabledBorder: inputBorder(k.outlineVariant.withValues(alpha: 0.5), 1),
        hintStyle: AppTypography.body.copyWith(color: k.text3),
        labelStyle: AppTypography.body.copyWith(color: k.text2),
        floatingLabelStyle: WidgetStateTextStyle.resolveWith(
          (states) => AppTypography.labelS.copyWith(
            color: states.contains(WidgetState.error)
                ? k.haksizText
                : states.contains(WidgetState.focused)
                    ? k.primaryText
                    : k.text2,
          ),
        ),
        helperStyle: AppTypography.meta.copyWith(color: k.text3),
        counterStyle: AppTypography.meta.copyWith(color: k.text3),
        errorStyle: AppTypography.meta.copyWith(color: k.haksizText),
        prefixIconColor: k.text2,
        suffixIconColor: k.text2,
        iconColor: k.text2,
        prefixStyle: AppTypography.body.copyWith(color: k.text2),
        suffixStyle: AppTypography.body.copyWith(color: k.text2),
      ),
      textSelectionTheme: TextSelectionThemeData(
        cursorColor: k.primary,
        selectionColor: k.primary.withValues(alpha: 0.28),
        selectionHandleColor: k.primary,
      ),
      chipTheme: ChipThemeData(
        color: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) return k.ink;
          if (states.contains(WidgetState.disabled)) {
            return k.muted.withValues(alpha: 0.5);
          }
          return k.muted;
        }),
        backgroundColor: k.muted,
        selectedColor: k.ink,
        disabledColor: k.muted.withValues(alpha: 0.5),
        checkmarkColor: k.onInk,
        deleteIconColor: k.text2,
        labelStyle: AppTypography.labelS.copyWith(
          color: WidgetStateColor.resolveWith(
            (states) => states.contains(WidgetState.selected)
                ? k.onInk
                : states.contains(WidgetState.disabled)
                    ? k.text3
                    : k.text1,
          ),
        ),
        secondaryLabelStyle: AppTypography.labelS.copyWith(color: k.onInk),
        iconTheme: IconThemeData(size: 16, color: k.text2),
        side: BorderSide.none,
        shape: const StadiumBorder(),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
        labelPadding: const EdgeInsets.symmetric(horizontal: 4),
        elevation: 0,
        pressElevation: 0,
        shadowColor: Colors.transparent,
        selectedShadowColor: Colors.transparent,
        surfaceTintColor: Colors.transparent,
        brightness: brightness,
      ),
      tabBarTheme: TabBarThemeData(
        indicator: BoxDecoration(
          color: k.surface,
          borderRadius: KararRadius.all(KararRadius.pill),
          boxShadow: KararShadow.soft(brightness),
          border: isDark ? Border.all(color: KararShadow.cardHairlineDark) : null,
        ),
        indicatorSize: TabBarIndicatorSize.tab,
        dividerColor: Colors.transparent,
        dividerHeight: 0,
        labelColor: k.text1,
        unselectedLabelColor: k.text2,
        labelStyle: AppTypography.label,
        unselectedLabelStyle: AppTypography.labelS,
        overlayColor: const WidgetStatePropertyAll(Colors.transparent),
        splashFactory: NoSplash.splashFactory,
        labelPadding: const EdgeInsets.symmetric(horizontal: 12),
      ),
      switchTheme: SwitchThemeData(
        thumbColor: const WidgetStatePropertyAll(Colors.white),
        trackColor: WidgetStateProperty.resolveWith((states) {
          final selected = states.contains(WidgetState.selected);
          final color = selected ? k.primary : k.muted;
          return states.contains(WidgetState.disabled)
              ? color.withValues(alpha: 0.5)
              : color;
        }),
        trackOutlineColor: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected)
              ? Colors.transparent
              : k.outline,
        ),
        trackOutlineWidth: const WidgetStatePropertyAll(1.5),
        overlayColor: overlay(base: k.primary, pressed: 0.12, focused: 0.12),
      ),
      checkboxTheme: CheckboxThemeData(
        shape: RoundedRectangleBorder(
          borderRadius: KararRadius.all(KararRadius.xs),
        ),
        fillColor: WidgetStateProperty.resolveWith(
          (states) =>
              states.contains(WidgetState.selected) ? k.primary : null,
        ),
        checkColor: WidgetStatePropertyAll(cs.onPrimary),
        side: WidgetStateBorderSide.resolveWith(
          (states) => states.contains(WidgetState.selected)
              ? BorderSide(color: k.primary, width: 1.5)
              : BorderSide(color: k.outline, width: 1.5),
        ),
      ),
      radioTheme: RadioThemeData(
        fillColor: WidgetStateProperty.resolveWith(
          (states) =>
              states.contains(WidgetState.selected) ? k.primary : k.outline,
        ),
      ),
      sliderTheme: SliderThemeData(
        activeTrackColor: k.primary,
        inactiveTrackColor: k.muted,
        thumbColor: k.primary,
        overlayColor: k.primary.withValues(alpha: 0.12),
      ),
      progressIndicatorTheme: ProgressIndicatorThemeData(
        color: k.primary,
        linearTrackColor: k.muted,
        circularTrackColor: Colors.transparent,
        refreshBackgroundColor: k.surface,
        strokeCap: StrokeCap.round,
      ),
      scrollbarTheme: ScrollbarThemeData(
        thumbColor: WidgetStatePropertyAll(k.text3.withValues(alpha: 0.45)),
        radius: const Radius.circular(KararRadius.pill),
      ),
    );
  }
}
