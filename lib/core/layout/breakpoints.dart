import 'package:flutter/widgets.dart';

abstract final class Breakpoints {
  static const double mobile = 600;
  static const double tablet = 1024;
  static const double navExpanded = 1263;
}

extension LayoutContext on BuildContext {
  double get screenWidth => MediaQuery.sizeOf(this).width;
  bool get isMobile => screenWidth < Breakpoints.mobile;
  bool get isTablet =>
      screenWidth >= Breakpoints.mobile && screenWidth < Breakpoints.tablet;
  bool get isDesktop => screenWidth >= Breakpoints.tablet;
  bool get isSideNavExpanded => screenWidth >= Breakpoints.navExpanded;

  /// Bottom space taken by the system inset and, inside the mobile/tablet
  /// shell, the floating pill nav (the shell uses `extendBody: true`, so the
  /// nav height is part of `MediaQuery.padding.bottom`).
  double get navBottomInset => MediaQuery.paddingOf(this).bottom;

  /// Padding for scrollables inside a shell tab that set an explicit
  /// `padding:`. ListView only adds the MediaQuery padding automatically when
  /// `padding` is null, so explicit paddings must clear the floating nav.
  EdgeInsets scrollPadding({double top = 8}) =>
      EdgeInsets.fromLTRB(0, top, 0, navBottomInset + 24);
}
