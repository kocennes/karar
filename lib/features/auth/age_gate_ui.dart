import 'package:go_router/go_router.dart';

import '../../core/auth/age_gate.dart';
import '../../core/auth/age_gate_coordinator.dart';
import 'widgets/age_gate_widgets.dart';

/// P0-02: binds [AgeGateCoordinator] to the app's router.
///
/// * Refusal: the neutral refusal page. Sign-up, sign-in and the birth-date
///   step show it in place for their own requests, so they are left alone.
/// * Verification required: the birth-date step on top of the current screen;
///   once done it pops back and the user can retry the action.
/// * Declaration required: the guest declaration sheet on the root navigator.
AgeGateUi routerAgeGateUi(GoRouter router) => AgeGateUi(
      showRefusal: () {
        if (_refusesInPlace.contains(_location(router))) return;
        router.go(ageRefusalPath);
      },
      showOnboarding: () {
        if (_location(router) == ageOnboardingPath) return;
        router.push<bool>(ageOnboardingPath);
      },
      askDeclaration: (declare) async {
        final context = router.routerDelegate.navigatorKey.currentContext;
        if (context == null || !context.mounted) return false;
        return showAgeDeclarationSheet(context, declare: declare);
      },
    );

const _refusesInPlace = {
  '/auth/login',
  '/auth/register',
  ageOnboardingPath,
  ageRefusalPath,
};

String _location(GoRouter router) {
  if (router.routerDelegate.currentConfiguration.isEmpty) return '';
  return router.state.matchedLocation;
}
