import 'package:flutter/material.dart';

import '../../core/theme/karar_tokens.dart';

/// Karar logo (spec §4.3): a purple gradient circle with a white
/// `balance_rounded` mark, plus the "karar" wordmark.
///
/// | size   | mark | icon | wordmark |
/// |--------|------|------|----------|
/// | small  | 28   | 14   | 15       |
/// | medium | 40   | 22   | 24       |
/// | large  | 48   | 24   | 26       |
class KararLogo extends StatelessWidget {
  const KararLogo({
    super.key,
    this.size = LogoSize.medium,
    this.showWordmark = true,
    this.wordmarkColor,
  });

  final LogoSize size;
  final bool showWordmark;

  /// Defaults to `kc.text1`; pass white on brand panels.
  final Color? wordmarkColor;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final markSize = switch (size) {
      LogoSize.small => 28.0,
      LogoSize.medium => 40.0,
      LogoSize.large => 48.0,
    };
    final iconSize = switch (size) {
      LogoSize.small => 14.0,
      LogoSize.medium => 22.0,
      LogoSize.large => 24.0,
    };
    final wordmarkSize = switch (size) {
      LogoSize.small => 15.0,
      LogoSize.medium => 24.0,
      LogoSize.large => 26.0,
    };
    final gap = switch (size) {
      LogoSize.small => 6.0,
      LogoSize.medium => 10.0,
      LogoSize.large => 10.0,
    };

    final mark = Container(
      width: markSize,
      height: markSize,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        gradient: KararGradients.logo,
        boxShadow: kc.isDark
            ? null
            : [
                BoxShadow(
                  color: const Color(0xFF7D50F0).withValues(alpha: 0.35),
                  offset: const Offset(0, 8),
                  blurRadius: 18,
                ),
              ],
      ),
      alignment: Alignment.center,
      child: Icon(Icons.balance_rounded, size: iconSize, color: Colors.white),
    );

    if (!showWordmark) {
      return Semantics(
        label: 'karar',
        image: true,
        child: ExcludeSemantics(child: mark),
      );
    }

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        ExcludeSemantics(child: mark),
        SizedBox(width: gap),
        Flexible(
          child: Text(
            'karar',
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontFamily: 'PlusJakartaSans',
              fontSize: wordmarkSize,
              fontWeight: FontWeight.w800,
              color: wordmarkColor ?? kc.text1,
              letterSpacing: -1.0,
              height: 1,
            ),
          ),
        ),
      ],
    );
  }
}

enum LogoSize { small, medium, large }
