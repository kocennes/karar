import 'package:flutter/material.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import 'karar_circle_button.dart';

/// Shows a Karar modal bottom sheet (top radius 30, theme surface, scrim
/// barrier). Uses the root navigator by default so the sheet covers the
/// floating pill nav of the shell.
///
/// Put a [KararSheetHeader] at the top of [builder]'s content for the
/// handle/title/close row (the Material drag handle is off by default to
/// avoid a double handle).
Future<T?> showKararSheet<T>(
  BuildContext context, {
  required WidgetBuilder builder,
  bool scrollControlled = false,
  bool useRootNavigator = true,
  bool showDragHandle = false,
  bool isDismissible = true,
  bool enableDrag = true,
  bool useSafeArea = true,
  Color? backgroundColor,
  BoxConstraints? constraints,
}) {
  return showModalBottomSheet<T>(
    context: context,
    builder: builder,
    isScrollControlled: scrollControlled,
    useRootNavigator: useRootNavigator,
    showDragHandle: showDragHandle,
    isDismissible: isDismissible,
    enableDrag: enableDrag,
    useSafeArea: useSafeArea,
    backgroundColor: backgroundColor,
    constraints: constraints ?? const BoxConstraints(maxWidth: 640),
  );
}

/// Header row of a Karar sheet: 40 × 5 handle 12 px from the top, title
/// (`appBarTitle`), optional count (`labelS` text3) and a close circle
/// (muted, 36 visual / 44 hit, tooltip "Kapat").
class KararSheetHeader extends StatelessWidget {
  const KararSheetHeader({
    super.key,
    required this.title,
    this.count,
    this.onClose,
    this.showHandle = true,
    this.trailing,
    this.padding = const EdgeInsets.fromLTRB(20, 0, 12, 6),
  });

  final String title;
  final String? count;

  /// Shows the close button when non-null.
  final VoidCallback? onClose;
  final bool showHandle;

  /// Extra action placed before the close button.
  final Widget? trailing;
  final EdgeInsets padding;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (showHandle)
          Padding(
            padding: const EdgeInsets.only(top: 12, bottom: 8),
            child: Center(
              child: ExcludeSemantics(
                child: Container(
                  width: 40,
                  height: 5,
                  decoration: BoxDecoration(
                    color: kc.outlineVariant,
                    borderRadius: KararRadius.all(3),
                  ),
                ),
              ),
            ),
          )
        else
          const SizedBox(height: 12),
        Padding(
          padding: padding,
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 44),
            child: Row(
              children: [
                Flexible(
                  child: Semantics(
                    header: true,
                    child: Text(
                      title,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: AppTypography.appBarTitle.copyWith(
                        color: kc.text1,
                      ),
                    ),
                  ),
                ),
                if (count != null) ...[
                  const SizedBox(width: 8),
                  Text(
                    count!,
                    style: AppTypography.labelS.copyWith(
                      color: kc.text3,
                      fontFeatures: AppTypography.tabular,
                    ),
                  ),
                ],
                const Spacer(),
                if (trailing != null) trailing!,
                if (onClose != null)
                  KararCircleButton(
                    icon: Icons.close_rounded,
                    onPressed: onClose,
                    tooltip: 'Kapat',
                    style: KararCircleStyle.muted,
                    size: 36,
                    iconSize: 20,
                  ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}
