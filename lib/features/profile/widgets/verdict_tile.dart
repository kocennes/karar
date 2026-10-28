import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../../core/layout/breakpoints.dart';
import '../../../core/theme/app_typography.dart';
import '../../../core/theme/category_style.dart';
import '../../../core/theme/karar_tokens.dart';
import '../../../core/utils/count_format.dart';
import '../../../shared/models/post.dart';
import '../../../shared/widgets/on_color_chip.dart';
import '../../../shared/widgets/post_surface.dart';
import 'count_pill.dart';

/// Profile grid tile (spec §6.5 `VerdictTile`): a small post surface
/// (category gradient or photo, radius 20) laid out like the reference's
/// caption cards:
///
/// * top-left: the category on a glass chip (icon disc below 600 px, icon +
///   name from 600 px); the owner "…" menu sits opposite it on the same row;
/// * text tiles: a small “ watermark under the chip, so the open gradient
///   reads as a designed card rather than an empty placeholder;
/// * bottom-left: the title (14/800 white) anchored right above the
///   [CountPill] / status [footer].
///
/// Photo tiles keep the upper photo vivid (only the §2.4 scrims): the title
/// gets its own caption veil anchored to the laid-out title block (black
/// 50 % at the first line, easing into the bottom scrim) plus a soft text
/// shadow: worst case (pure-white photo, 5 lines on a 175 px tile) ≥ 5.5:1.
///
/// Lives in the profile feature (used by the profile tabs, my posts, saved
/// posts and other profiles).
/// TODO(owner D/shared): promote to lib/shared/widgets/verdict_tile.dart if
/// another area needs it.
class VerdictTile extends StatelessWidget {
  const VerdictTile({
    super.key,
    required this.post,
    required this.onTap,
    this.trailingAction,
    this.footer,
  });

  final Post post;
  final VoidCallback onTap;

  /// Optional 48 × 48 control in the top-right corner (owner "…" menu)
  /// whose visual is a [menuDiameter] circle. It is centred on the
  /// category row, so it never meets the (bottom-anchored) title.
  final Widget? trailingAction;

  /// Replaces the bottom [CountPill] (e.g. a moderation status panel).
  final Widget? footer;

  /// Visual diameter of the [trailingAction] circle (and of the category
  /// disc on narrow tiles).
  static const double menuDiameter = 30;

  /// Content inset from the tile edge.
  static const double _inset = 12;

  /// Offset of the 48 × 48 [trailingAction] box so its circle shares the
  /// category row's inset and centre line.
  static const double _menuOffset = _inset - (48 - menuDiameter) / 2;

  /// Max title lines on photo tiles, so the upper photo stays visible.
  static const int _photoMaxLines = 5;

  /// Grid delegate for verdict tiles: 2 columns below 600 px (aspect 0.8),
  /// 3 columns from 600 px (aspect 0.72), 8 px gaps.
  static SliverGridDelegate gridDelegate(BuildContext context) {
    final wide = context.screenWidth >= Breakpoints.mobile;
    return SliverGridDelegateWithFixedCrossAxisCount(
      crossAxisCount: wide ? 3 : 2,
      mainAxisSpacing: 8,
      crossAxisSpacing: 8,
      childAspectRatio: wide ? 0.72 : 0.8,
    );
  }

  /// The verdict read-out shown in the pill: the winning side with its
  /// percentage once `showPercentage` (≥ 40 votes), else the vote count.
  static ({String label, IconData? icon, bool? hakli}) verdictOf(Post post) {
    if (!post.showPercentage) {
      return (
        label: '${formatCompactCount(post.totalVotes)} oy',
        icon: null,
        hakli: null,
      );
    }
    final p = post.hakliPercent;
    if (p >= 50) {
      return (
        label: '${formatPercent(p)} Haklı',
        icon: Icons.check_rounded,
        hakli: true
      );
    }
    return (
      label: '${formatPercent(100 - p)} Haksız',
      icon: Icons.close_rounded,
      hakli: false,
    );
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final verdict = verdictOf(post);
    final isPhoto = PostSurface.isPhotoPost(post);
    final category = CategoryStyle.of(post.category);
    final categoryName = post.category.name.trim();
    final showCategoryName =
        context.screenWidth >= Breakpoints.mobile && categoryName.isNotEmpty;
    final radius = KararRadius.all(KararRadius.tile);
    var titleStyle = AppTypography.labelS.copyWith(
      fontSize: 14,
      height: 1.3,
      fontWeight: FontWeight.w800,
      color: kc.onColor,
    );
    if (isPhoto) {
      titleStyle = titleStyle.copyWith(
        shadows: [
          Shadow(
            color: kc.photoVeil.withValues(alpha: 0.4),
            offset: const Offset(0, 1),
            blurRadius: 3,
          ),
        ],
      );
    }

    final content = Padding(
      padding: const EdgeInsets.all(_inset),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          // Category row; the owner menu (if any) is overlaid at its end.
          ExcludeSemantics(
            child: Row(
              children: [
                Flexible(
                  child: showCategoryName
                      ? OnColorChip(icon: category.icon, label: categoryName)
                      : _CategoryDisc(icon: category.icon, onPhoto: isPhoto),
                ),
                if (trailingAction != null)
                  const SizedBox(width: menuDiameter + 6),
              ],
            ),
          ),
          const SizedBox(height: 8),
          Expanded(
            child: _TileTitle(
              text: post.title,
              style: titleStyle,
              maxLines: isPhoto ? _photoMaxLines : null,
              veil: isPhoto ? kc.photoVeil.withValues(alpha: 0.5) : null,
              veilBleed: _inset,
            ),
          ),
          const SizedBox(height: 10),
          footer ??
              Align(
                alignment: AlignmentDirectional.centerStart,
                child: CountPill(
                  label: verdict.label,
                  icon: verdict.icon,
                  iconColor: verdict.hakli == null
                      ? null
                      : (verdict.hakli!
                          ? kc.onCapsuleHakli
                          : kc.onCapsuleHaksiz),
                ),
              ),
        ],
      ),
    );

    return Semantics(
      container: true,
      explicitChildNodes: true,
      child: PostSurface(
        post: post,
        radius: KararRadius.tile,
        showQuote: false,
        showRailSheen: false,
        child: Stack(
          fit: StackFit.expand,
          children: [
            // Text tiles: a smaller “ watermark than the feed card's 150 px
            // one, tucked under the category chip.
            if (!isPhoto)
              PositionedDirectional(
                start: _inset - 2,
                top: _inset + menuDiameter,
                child: IgnorePointer(
                  child: ExcludeSemantics(
                    child: Text(
                      '“',
                      textScaler: TextScaler.noScaling,
                      style: TextStyle(
                        fontFamily: AppTypography.fontFamily,
                        fontSize: 96,
                        height: 1,
                        fontWeight: FontWeight.w800,
                        color: kc.quoteWatermark,
                      ),
                    ),
                  ),
                ),
              ),
            Semantics(
              button: true,
              label: 'Gönderi: ${post.title}',
              value: verdict.label,
              excludeSemantics: footer == null,
              child: Material(
                type: MaterialType.transparency,
                child: InkWell(
                  onTap: onTap,
                  borderRadius: radius,
                  splashColor: kc.splashOnColor,
                  highlightColor: kc.highlightOnColor,
                  child: content,
                ),
              ),
            ),
            if (trailingAction != null)
              PositionedDirectional(
                top: _menuOffset,
                end: _menuOffset,
                child: trailingAction!,
              ),
          ],
        ),
      ),
    );
  }
}

/// Icon-only category marker for narrow tiles: a [VerdictTile.menuDiameter]
/// glass disc (same fill as `OnColorChip`, darker on photos, solid ink in
/// high contrast) with the white category icon.
class _CategoryDisc extends StatelessWidget {
  const _CategoryDisc({required this.icon, required this.onPhoto});

  final IconData icon;
  final bool onPhoto;

  @override
  Widget build(BuildContext context) {
    final highContrast = MediaQuery.maybeHighContrastOf(context) ?? false;
    final fill = highContrast
        ? KararGlass.chipHighContrast
        : onPhoto
            ? KararGlass.chipOnPhoto
            : KararGlass.chipOnColor;
    return Container(
      width: VerdictTile.menuDiameter,
      height: VerdictTile.menuDiameter,
      decoration: BoxDecoration(color: fill, shape: BoxShape.circle),
      child: Icon(icon, size: 16, color: context.kc.onColor),
    );
  }
}

/// Bottom-anchored tile title. Max lines are computed from the height left
/// above the pill (capped by [maxLines] when set).
///
/// With a [veil] colour (photo tiles) a caption veil is painted behind the
/// laid-out title block: transparent 28 px above the first line → [veil] at
/// the first line → transparent 56 px lower, where the §2.4 bottom scrim
/// takes over. It bleeds [veilBleed] px to each side so it spans the full
/// tile width (the tile's rounded clip trims it), and it never rises above
/// this widget, so the category row stays clear.
class _TileTitle extends StatelessWidget {
  const _TileTitle({
    required this.text,
    required this.style,
    this.maxLines,
    this.veil,
    this.veilBleed = 0,
  });

  final String text;
  final TextStyle style;
  final int? maxLines;
  final Color? veil;
  final double veilBleed;

  @override
  Widget build(BuildContext context) {
    final scaler = MediaQuery.textScalerOf(context);
    final direction = Directionality.of(context);
    // Measure with the same effective style the Text widget renders with.
    final effective = DefaultTextStyle.of(context).style.merge(style);

    return LayoutBuilder(
      builder: (context, constraints) {
        final probe = TextPainter(
          text: TextSpan(text: 'Ağ', style: effective),
          textDirection: direction,
          textScaler: scaler,
          maxLines: 1,
        )..layout();
        final lineHeight = math.max(1.0, probe.height);
        probe.dispose();

        var lines = constraints.maxHeight.isFinite
            ? math.max(1, (constraints.maxHeight / lineHeight).floor())
            : 5;
        if (maxLines != null) lines = math.min(lines, maxLines!);

        final title = Align(
          alignment: AlignmentDirectional.bottomStart,
          child: Text(
            text,
            maxLines: lines,
            overflow: TextOverflow.ellipsis,
            style: style,
          ),
        );

        final veilColor = veil;
        if (veilColor == null || !constraints.maxWidth.isFinite) return title;

        final measure = TextPainter(
          text: TextSpan(text: text, style: effective),
          textDirection: direction,
          textScaler: scaler,
          maxLines: lines,
          ellipsis: '…',
        )..layout(maxWidth: constraints.maxWidth);
        final textHeight = measure.height;
        measure.dispose();

        return CustomPaint(
          painter: _CaptionVeilPainter(
            color: veilColor,
            textHeight: textHeight,
            bleed: veilBleed,
          ),
          child: title,
        );
      },
    );
  }
}

class _CaptionVeilPainter extends CustomPainter {
  _CaptionVeilPainter({
    required this.color,
    required this.textHeight,
    required this.bleed,
  });

  final Color color;
  final double textHeight;
  final double bleed;

  /// Fade-in above the first line.
  static const double _fade = 28;

  /// Fade-out below the first line (about three lines). Further down the
  /// §2.4 bottom scrim (52 % → 80 %) already carries the contrast, so the
  /// veil stops there instead of turning the tile bottom black.
  static const double _decay = 56;

  @override
  void paint(Canvas canvas, Size size) {
    final textTop = math.max(0.0, size.height - textHeight);
    final fade = math.min(_fade, textTop);
    final rect = Rect.fromLTRB(
      -bleed,
      textTop - fade,
      size.width + bleed,
      textTop + _decay,
    );
    final total = rect.height;
    if (total <= 0) return;
    final shader = LinearGradient(
      begin: Alignment.topCenter,
      end: Alignment.bottomCenter,
      colors: [
        color.withValues(alpha: 0),
        color,
        color.withValues(alpha: 0),
      ],
      stops: [0, fade / total, 1],
    ).createShader(rect);
    canvas.drawRect(rect, Paint()..shader = shader);
  }

  @override
  bool shouldRepaint(_CaptionVeilPainter old) =>
      old.color != color ||
      old.textHeight != textHeight ||
      old.bleed != bleed;
}

/// Bottom panel replacing the count pill when a post is not active
/// (under review, hidden, removed): black veil, white icon + label + reason.
class TileStatusPanel extends StatelessWidget {
  const TileStatusPanel({
    super.key,
    required this.icon,
    required this.label,
    this.reason,
  });

  final IconData icon;
  final String label;
  final String? reason;

  @override
  Widget build(BuildContext context) {
    final onColor = context.kc.onColor;
    return Container(
      padding: const EdgeInsets.fromLTRB(10, 8, 10, 8),
      decoration: BoxDecoration(
        color: KararGlass.chipOnPhoto,
        borderRadius: KararRadius.all(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            children: [
              Icon(icon, size: 14, color: onColor),
              const SizedBox(width: 6),
              Flexible(
                child: Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.micro.copyWith(color: onColor),
                ),
              ),
            ],
          ),
          if (reason != null && reason!.isNotEmpty) ...[
            const SizedBox(height: 2),
            Text(
              reason!,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: AppTypography.meta.copyWith(
                color: onColor,
                fontSize: 11,
                height: 1.3,
              ),
            ),
          ],
        ],
      ),
    );
  }
}
