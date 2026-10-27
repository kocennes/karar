import 'package:flutter/material.dart';

import '../../../core/theme/app_typography.dart';
import '../../../core/theme/karar_tokens.dart';
import '../../../shared/widgets/karar_surface.dart';

/// Grouped-list primitives (spec §6.9), used by settings, the guest profile
/// and other settings-like screens:
///
/// * [SettingsSectionHeader] — sentence-case 13/800 text3 header.
/// * [SettingsGroup] — an inset `KararSurface(radius 24, padding 4)` card
///   with 1 px hairlines inset 64 between its rows.
/// * [SettingsRow] — min 60 px row: 36 px rounded-square icon tile
///   (primaryContainer + primary icon; destructive rows haksizSoft +
///   haksizFill), `bodyStrong` 16/700 title, `bodyS` text2 subtitle and a
///   trailing switch / chevron / value.

/// Section header: sentence case (never upper-cased), 13/800 text3.
/// Starts at 32 px, the left edge of the rows' icon tiles (group margin 16
/// + card padding 4 + row padding 12), so header, tiles and titles share
/// one column instead of three different left edges.
class SettingsSectionHeader extends StatelessWidget {
  const SettingsSectionHeader(this.title, {super.key});

  final String title;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(32, 20, 24, 8),
      child: Semantics(
        header: true,
        child: Text(
          title,
          style: AppTypography.labelS.copyWith(
            fontWeight: FontWeight.w800,
            color: context.kc.text3,
          ),
        ),
      ),
    );
  }
}

/// Inset card holding [SettingsRow]s separated by hairlines (inset 64).
/// Children that render nothing should be left out of [children] (a
/// divider is drawn between every pair).
class SettingsGroup extends StatelessWidget {
  const SettingsGroup({
    super.key,
    required this.children,
    this.margin = const EdgeInsets.symmetric(horizontal: 16),
  });

  final List<Widget> children;
  final EdgeInsetsGeometry margin;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final items = <Widget>[];
    for (var i = 0; i < children.length; i++) {
      if (i > 0) {
        items.add(Divider(
          height: 1,
          thickness: 1,
          indent: 64,
          endIndent: 12,
          color: kc.outlineVariant,
        ));
      }
      items.add(children[i]);
    }
    return KararSurface(
      margin: margin,
      radius: KararRadius.surface,
      padding: const EdgeInsets.all(4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: items,
      ),
    );
  }
}

/// One row of a [SettingsGroup].
class SettingsRow extends StatelessWidget {
  const SettingsRow({
    super.key,
    required this.icon,
    required this.title,
    this.subtitle,
    this.trailing,
    this.value,
    this.onTap,
    this.destructive = false,
    this.titleColor,
    this.showChevron = true,
  });

  final IconData icon;
  final String title;
  final String? subtitle;

  /// Switch, button or any custom trailing widget.
  final Widget? trailing;

  /// Short value shown on the right (`labelS` text2) when no [trailing].
  final String? value;
  final VoidCallback? onTap;

  /// Haksız-tinted icon tile and title.
  final bool destructive;

  /// Overrides the title colour (e.g. a warning row).
  final Color? titleColor;

  /// Shows a chevron for tappable rows without [trailing] / [value].
  final bool showChevron;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final tileBg = destructive ? kc.haksizSoft : kc.primaryContainer;
    final tileFg = destructive ? kc.haksizFill : kc.primary;
    final effectiveTitleColor =
        titleColor ?? (destructive ? kc.haksizText : kc.text1);

    Widget? trailingWidget = trailing;
    if (trailingWidget == null && value != null) {
      trailingWidget = ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 160),
        child: Text(
          value!,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          textAlign: TextAlign.end,
          style: AppTypography.labelS.copyWith(color: kc.text2),
        ),
      );
    }
    if (trailingWidget == null && onTap != null && showChevron) {
      trailingWidget = Icon(
        Icons.chevron_right_rounded,
        size: 22,
        color: kc.text3,
      );
    }

    final row = ConstrainedBox(
      constraints: const BoxConstraints(minHeight: 60),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
        child: Row(
          children: [
            ExcludeSemantics(
              child: Container(
                width: 36,
                height: 36,
                decoration: BoxDecoration(
                  color: tileBg,
                  borderRadius: KararRadius.all(KararRadius.sm),
                ),
                child: Icon(icon, size: 20, color: tileFg),
              ),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    title,
                    style: AppTypography.bodyStrong.copyWith(
                      fontSize: 16,
                      height: 22 / 16,
                      color: effectiveTitleColor,
                    ),
                  ),
                  if (subtitle != null && subtitle!.isNotEmpty) ...[
                    const SizedBox(height: 2),
                    // 13/500 so most subtitles fit on one line beside a
                    // switch instead of leaving an orphaned last word.
                    Text(
                      subtitle!,
                      style: AppTypography.bodyS.copyWith(
                        fontSize: 13,
                        height: 18 / 13,
                        color: kc.text2,
                      ),
                    ),
                  ],
                ],
              ),
            ),
            if (trailingWidget != null) ...[
              const SizedBox(width: 8),
              trailingWidget,
            ],
          ],
        ),
      ),
    );

    // Rows with their own interactive trailing control (switch, button)
    // keep it as a separate semantics node; plain rows read as one item.
    final merge = trailing == null;
    Widget result = row;
    if (onTap != null) {
      result = Material(
        type: MaterialType.transparency,
        child: InkWell(
          onTap: onTap,
          borderRadius: KararRadius.all(KararRadius.tile),
          child: row,
        ),
      );
    }
    return merge ? MergeSemantics(child: result) : result;
  }
}
