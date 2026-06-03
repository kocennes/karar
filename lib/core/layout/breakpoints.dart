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
}
