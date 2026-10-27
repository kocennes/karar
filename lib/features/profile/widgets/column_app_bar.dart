import 'dart:math' as math;

import 'package:flutter/material.dart';

/// An [AppBar] whose back button, title and actions line up with the
/// centred content column (`CenteredContent(maxWidth: 680)`) whenever the
/// page is wider than that column (desktop and wide tablets), instead of
/// sitting at the far edges of the window. At or below the column width it
/// is a plain [AppBar] with the theme's insets, so mobile is unchanged.
///
/// Used by the profile, notifications and settings screens.
/// TODO(owner A/shared): promote to lib/shared/widgets if other areas need it.
class ColumnAppBar extends StatelessWidget implements PreferredSizeWidget {
  const ColumnAppBar({
    super.key,
    this.title,
    this.actions,
    this.leading,
    this.automaticallyImplyLeading = true,
    this.centerTitle,
    this.titleSpacing,
    this.toolbarHeight,
    this.bottom,
    this.maxWidth = 680,
  });

  final Widget? title;
  final List<Widget>? actions;
  final Widget? leading;
  final bool automaticallyImplyLeading;
  final bool? centerTitle;
  final double? titleSpacing;
  final double? toolbarHeight;
  final PreferredSizeWidget? bottom;

  /// Width of the content column the bar aligns with.
  final double maxWidth;

  /// Horizontal inset of the visual icons from the column edge (the 48 px
  /// icon buttons carry 12 px of their own padding, so icons line up with
  /// the 16 px content gutter).
  static const double _actionsEndInset = 4;

  // Delegates to an AppBar so the Scaffold sizes the slot from the theme's
  // toolbar height (`AppBar.preferredHeightFor`).
  @override
  Size get preferredSize =>
      AppBar(toolbarHeight: toolbarHeight, bottom: bottom).preferredSize;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final inset = constraints.maxWidth.isFinite
            ? math.max(0.0, (constraints.maxWidth - maxWidth) / 2)
            : 0.0;

        if (inset < 1) {
          return AppBar(
            title: title,
            actions: actions,
            leading: leading,
            automaticallyImplyLeading: automaticallyImplyLeading,
            centerTitle: centerTitle,
            titleSpacing: titleSpacing,
            toolbarHeight: toolbarHeight,
            bottom: bottom,
          );
        }

        // Same implied leading as AppBar (back, or close for full-screen
        // dialogs), moved in to the column edge.
        final route = ModalRoute.of(context);
        Widget? lead = leading;
        if (lead == null &&
            automaticallyImplyLeading &&
            (route?.impliesAppBarDismissal ?? false)) {
          final useClose = route is PageRoute && route.fullscreenDialog;
          lead = useClose ? const CloseButton() : const BackButton();
        }

        return AppBar(
          automaticallyImplyLeading: false,
          leading: lead == null
              ? null
              : Padding(
                  padding: EdgeInsetsDirectional.only(start: inset),
                  child: Center(child: lead),
                ),
          leadingWidth: lead == null ? null : inset + 56,
          title: title,
          centerTitle: centerTitle,
          titleSpacing: lead == null
              ? inset + 16
              : (titleSpacing ?? NavigationToolbar.kMiddleSpacing),
          actions: actions,
          actionsPadding:
              EdgeInsetsDirectional.only(end: inset + _actionsEndInset),
          toolbarHeight: toolbarHeight,
          bottom: bottom,
        );
      },
    );
  }
}
