import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';

class KararLogo extends StatelessWidget {
  const KararLogo({super.key, this.size = LogoSize.medium});

  final LogoSize size;

  @override
  Widget build(BuildContext context) {
    final iconBoxSize = switch (size) {
      LogoSize.small => 28.0,
      LogoSize.medium => 43.0,
      LogoSize.large => 48.0,
    };
    final scaleIconSize = switch (size) {
      LogoSize.small => 14.0,
      LogoSize.medium => 22.0,
      LogoSize.large => 24.0,
    };
    final badgeSize = switch (size) {
      LogoSize.small => 10.0,
      LogoSize.medium => 16.0,
      LogoSize.large => 17.0,
    };
    final badgeIconSize = switch (size) {
      LogoSize.small => 6.0,
      LogoSize.medium => 10.0,
      LogoSize.large => 11.0,
    };
    final wordmarkSize = switch (size) {
      LogoSize.small => 15.0,
      LogoSize.medium => 24.0,
      LogoSize.large => 26.0,
    };
    final gap = switch (size) {
      LogoSize.small => 6.0,
      LogoSize.medium => 8.0,
      LogoSize.large => 10.0,
    };

    final isDark = Theme.of(context).brightness == Brightness.dark;
    final boxColor =
        isDark ? AppColors.darkSurfaceVariant : AppColors.surfaceVariant;
    final boxBorder = isDark ? AppColors.darkBorder : AppColors.border;
    final scaleColor =
        isDark ? AppColors.darkTextPrimary : AppColors.textPrimary;
    final wordmarkColor =
        isDark ? AppColors.darkTextPrimary : AppColors.textPrimary;
    final hakliColor = isDark ? AppColors.darkHakli : AppColors.hakli;
    final haksizColor = isDark ? AppColors.darkHaksiz : AppColors.haksiz;

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        SizedBox(
          width: iconBoxSize + badgeSize * 0.6,
          height: iconBoxSize + badgeSize * 0.6,
          child: Stack(
            children: [
              Positioned(
                bottom: 0,
                right: 0,
                child: Container(
                  width: iconBoxSize,
                  height: iconBoxSize,
                  decoration: BoxDecoration(
                    color: boxColor,
                    borderRadius: BorderRadius.circular(iconBoxSize * 0.28),
                    border: Border.all(color: boxBorder),
                  ),
                  child: Center(
                    child: Icon(
                      Icons.balance_rounded,
                      size: scaleIconSize,
                      color: scaleColor,
                    ),
                  ),
                ),
              ),
              Positioned(
                top: 0,
                left: 0,
                child: Container(
                  width: badgeSize,
                  height: badgeSize,
                  decoration: BoxDecoration(
                    color: hakliColor,
                    shape: BoxShape.circle,
                  ),
                  child: Center(
                    child: Icon(Icons.check,
                        size: badgeIconSize, color: Colors.white),
                  ),
                ),
              ),
              Positioned(
                bottom: 0,
                right: 0,
                child: Container(
                  width: badgeSize,
                  height: badgeSize,
                  decoration: BoxDecoration(
                    color: haksizColor,
                    shape: BoxShape.circle,
                  ),
                  child: Center(
                    child: Icon(Icons.close,
                        size: badgeIconSize, color: Colors.white),
                  ),
                ),
              ),
            ],
          ),
        ),
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
              color: wordmarkColor,
              letterSpacing: 0,
              height: 1,
            ),
          ),
        ),
      ],
    );
  }
}

enum LogoSize { small, medium, large }
