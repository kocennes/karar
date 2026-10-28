import 'package:flutter/material.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import 'karar_pressable.dart';

/// Reference-style text tabs (spec §4.3): active `tab` 17/800 text1,
/// inactive 17/700 text3, an 18 × 3 `primary` underline that slides under
/// the active label (`base`), row height 44, `selected` semantics.
/// Scrolls horizontally when the labels do not fit (large text scale).
class KararTextTabs extends StatelessWidget {
  const KararTextTabs({
    super.key,
    required this.labels,
    required this.index,
    required this.onChanged,
    this.padding = EdgeInsets.zero,
    this.gap = 20,
  });

  final List<String> labels;
  final int index;
  final ValueChanged<int> onChanged;
  final EdgeInsets padding;
  final double gap;

  static const double _underlineWidth = 18;
  static const double _underlineHeight = 3;
  static const double _vPad = 7;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final scaler = MediaQuery.textScalerOf(context);
    final direction = Directionality.of(context);

    // Measure label widths so the underline can slide between them.
    final widths = <double>[];
    for (var i = 0; i < labels.length; i++) {
      final painter = TextPainter(
        text: TextSpan(
          text: labels[i],
          style: i == index ? AppTypography.tab : AppTypography.tabInactive,
        ),
        textDirection: direction,
        textScaler: scaler,
        maxLines: 1,
      )..layout();
      widths.add(painter.width);
      painter.dispose();
    }

    double underlineLeft = 0;
    final safeIndex = labels.isEmpty ? 0 : index.clamp(0, labels.length - 1);
    for (var i = 0; i < safeIndex; i++) {
      underlineLeft += widths[i] + gap;
    }
    if (labels.isNotEmpty) {
      underlineLeft += (widths[safeIndex] - _underlineWidth) / 2;
    }

    final row = Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        for (var i = 0; i < labels.length; i++) ...[
          if (i > 0) SizedBox(width: gap),
          KararPressable(
            onTap: () {
              if (i != index) {
                KararHaptics.toggle();
                onChanged(i);
              }
            },
            selected: i == index,
            semanticLabel: labels[i],
            pressedScale: 0.97,
            focusRadius: 6,
            minSize: const Size(0, 44),
            child: Padding(
              padding: const EdgeInsets.only(
                top: _vPad,
                bottom: _vPad + _underlineHeight + 5,
              ),
              child: AnimatedDefaultTextStyle(
                duration: KararMotion.d(context, KararMotion.fast),
                style: (i == index
                        ? AppTypography.tab
                        : AppTypography.tabInactive)
                    .copyWith(color: i == index ? kc.text1 : kc.text3),
                child: Text(labels[i], maxLines: 1, softWrap: false),
              ),
            ),
          ),
        ],
      ],
    );

    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      padding: padding,
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          row,
          if (labels.isNotEmpty)
            AnimatedPositioned(
              duration: KararMotion.d(context, KararMotion.base),
              curve: KararMotion.standard,
              left: underlineLeft,
              bottom: _vPad,
              width: _underlineWidth,
              height: _underlineHeight,
              child: ExcludeSemantics(
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    color: kc.primary,
                    borderRadius: KararRadius.all(2),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
