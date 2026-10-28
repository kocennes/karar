import '../api/api_client.dart';
import '../api/api_exception.dart';
import 'age_gate.dart';
import 'auth_service.dart';

/// What the app shows for the age gate. Bound once by the app shell
/// (`routerAgeGateUi`), so no screen or provider handles the codes itself.
class AgeGateUi {
  const AgeGateUi({
    required this.showRefusal,
    required this.showOnboarding,
    required this.askDeclaration,
  });

  /// The neutral refusal page.
  final void Function() showRefusal;

  /// The birth-date step for the signed-in account.
  final void Function() showOnboarding;

  /// The guest declaration sheet. [declare] sends the picked date
  /// (`POST /devices/age-declaration`); completes true once it succeeded,
  /// false when the sheet was dismissed or the date was refused.
  final Future<bool> Function(Future<void> Function(DateTime dateOfBirth)
      declare) askDeclaration;
}

/// P0-02: the single answer to the age-gate error codes, run by [ApiClient]
/// for every request (see [AgeGateHooks]):
///
/// * `AGE_NOT_ELIGIBLE` → lock the device and end any session
///   ([AuthService.applyAgeRefusal]), then the neutral refusal page.
/// * `AGE_VERIFICATION_REQUIRED` → mark the account and open the birth-date
///   step; the user retries the action afterwards.
/// * `AGE_DECLARATION_REQUIRED` → the guest declaration sheet; on success the
///   request is retried once, on a refusal the refusal above.
class AgeGateCoordinator implements AgeGateHooks {
  AgeGateCoordinator({required AuthService authService}) : _auth = authService;

  final AuthService _auth;
  AgeGateUi? _ui;

  void bindUi(AgeGateUi ui) => _ui = ui;

  /// Set while a refusal is being applied: its own logout requests may be
  /// answered `AGE_NOT_ELIGIBLE` too.
  bool _refusing = false;

  /// Set while the declaration sheet is open; a refusal then waits for the
  /// sheet to close before the refusal page replaces the screen under it.
  bool _declaring = false;
  bool _refusalPending = false;
  Future<bool>? _declaration;

  @override
  Future<void> onNotEligible() async {
    if (_refusing) return;
    _refusing = true;
    final bool changed;
    try {
      changed = await _auth.applyAgeRefusal();
    } finally {
      _refusing = false;
    }
    // Nothing new (a device refused before, e.g. a background request): the
    // caller's message is the refusal text; the page is not forced again.
    if (!changed) return;
    if (_declaring) {
      _refusalPending = true;
    } else {
      _ui?.showRefusal();
    }
  }

  @override
  Future<void> onVerificationRequired() async {
    // Without a session the server answers 401, handled as usual.
    if (!_auth.isLoggedIn) return;
    final alreadyAsked = _auth.currentUser?.requiresOnboarding == true;
    _auth.markRequiresOnboarding();
    if (!alreadyAsked) _ui?.showOnboarding();
  }

  /// Concurrent guest votes share one sheet.
  @override
  Future<bool> onDeclarationRequired() =>
      _declaration ??= _declare().whenComplete(() => _declaration = null);

  Future<bool> _declare() async {
    var locked = false;
    try {
      locked = await _auth.isAgeGateLocked();
    } catch (_) {}
    if (locked) {
      // Refused before: no second declaration from this device.
      await onNotEligible();
      throw ageRefusalError;
    }

    final ui = _ui;
    if (ui == null) return false;

    var refused = false;
    Future<void> declare(DateTime dateOfBirth) async {
      try {
        await _auth.declareDeviceAge(dateOfBirth);
      } on ApiException catch (e) {
        // The lock and the session were handled by [onNotEligible] already.
        if (e.code == AgeGate.notEligibleCode) refused = true;
        rethrow;
      }
    }

    _declaring = true;
    var declared = false;
    try {
      declared = await ui.askDeclaration(declare);
    } finally {
      _declaring = false;
    }
    if (refused || _refusalPending) {
      _refusalPending = false;
      ui.showRefusal();
      throw ageRefusalError;
    }
    return declared;
  }
}
