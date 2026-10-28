import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:karar/core/api/api_client.dart';
import 'package:karar/core/api/api_exception.dart';
import 'package:karar/core/auth/age_gate.dart';
import 'package:karar/core/auth/auth_service.dart';
import 'package:karar/core/storage/secure_storage.dart';
import 'package:karar/core/theme/app_theme.dart';
import 'package:karar/features/auth/age_onboarding_screen.dart';
import 'package:karar/features/auth/register_screen.dart';

/// Network-free AuthService: real secure-storage lock (mocked platform
/// storage), fake API calls.
class _FakeAuthService extends AuthService {
  _FakeAuthService({this.user})
      : super(
          storage: SecureStorage(),
          apiClient: ApiClient(baseUrl: 'http://127.0.0.1:9'),
        );

  AuthUser? user;
  int registerCalls = 0;
  int logoutCalls = 0;
  final declared = <DateTime>[];
  final onboardingDates = <DateTime>[];

  @override
  AuthUser? get currentUser => user;

  @override
  bool get isLoggedIn => user != null;

  @override
  Future<bool> isUsernameAvailable(String username) async => true;

  @override
  Future<void> declareDeviceAge(DateTime dateOfBirth) async =>
      declared.add(dateOfBirth);

  @override
  Future<String> register({
    required String username,
    required String email,
    required String password,
    required DateTime dateOfBirth,
    required String gender,
    required bool acceptedTerms,
    required bool acceptedCommunityGuidelines,
  }) async {
    registerCalls++;
    return email;
  }

  // Mirrors the server: an ineligible date → 403 AGE_NOT_ELIGIBLE, the
  // service locks the device and ends the session.
  @override
  Future<void> completeOnboarding({
    required DateTime dateOfBirth,
    required bool acceptedTerms,
    required bool acceptedCommunityGuidelines,
  }) async {
    onboardingDates.add(dateOfBirth);
    if (!AgeGate.isEligible(dateOfBirth)) {
      await lockAgeGate();
      await logout();
      throw const ApiException(
        statusCode: 403,
        code: AgeGate.notEligibleCode,
        message: AgeGate.refusalMessage,
      );
    }
    user = AuthUser.fromJson({
      'id': 'u1',
      'username': 'kararci',
      'email': 'k@example.com',
      'requiresOnboarding': false,
    });
  }

  @override
  Future<void> logout({LogoutReason reason = LogoutReason.manual}) async {
    logoutCalls++;
    user = null;
  }
}

AuthUser _googleUserNeedingOnboarding() => AuthUser.fromJson({
      // Shape of the 201 body of POST /api/v1/auth/google for a new account.
      'userId': 'u1',
      'username': 'kararci',
      'accessToken': 'a',
      'refreshToken': 'r',
      'isNewUser': true,
      'requiresOnboarding': true,
      'user': {
        'id': 'u1',
        'username': 'kararci',
        'email': 'k@example.com',
        'karma': 0,
        'authProvider': 'google',
        'emailVerified': true,
      },
    });

Future<void> _useTallPhone(WidgetTester tester) async {
  tester.view.physicalSize = const Size(800, 2600);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(tester.view.reset);
}

// The FlutterTest font draws every glyph 1em wide, so the register screen's
// longest gender option ("Belirtmek İstemiyorum") overflows its dropdown in
// tests only; a slightly smaller text scale keeps layout errors meaningful.
Widget _scaled(BuildContext context, Widget? child) => MediaQuery(
      data: MediaQuery.of(context)
          .copyWith(textScaler: const TextScaler.linear(0.85)),
      child: child!,
    );

Widget _app(Widget home) =>
    MaterialApp(theme: AppTheme.light(), builder: _scaled, home: home);

DateTime _yearsAgo(int years) {
  final now = DateTime.now();
  return DateTime(now.year - years, now.month, now.day);
}

String _usDate(DateTime d) =>
    '${d.month.toString().padLeft(2, '0')}/${d.day.toString().padLeft(2, '0')}/${d.year}';

Future<void> _pickBirthDate(WidgetTester tester, DateTime date) async {
  await tester.tap(find.widgetWithText(TextFormField, 'Doğum Tarihi'));
  await tester.pumpAndSettle();
  // Switch the picker to text input and type the date.
  await tester.tap(find.byIcon(Icons.edit_outlined));
  await tester.pumpAndSettle();
  await tester.enterText(
    find.descendant(
      of: find.byType(DatePickerDialog),
      matching: find.byType(TextField),
    ),
    _usDate(date),
  );
  await tester.tap(find.text('OK'));
  await tester.pumpAndSettle();
}

Future<String?> _storedLock() => SecureStorage().readAgeGateLock();

void main() {
  setUp(() => FlutterSecureStorage.setMockInitialValues({}));

  group('AgeGate (pure)', () {
    test('eligibility turns on exactly at the birthday', () {
      final now = DateTime(2026, 10, 28, 15);
      expect(AgeGate.isEligible(DateTime(2008, 10, 28), now: now), isTrue);
      expect(AgeGate.isEligible(DateTime(2008, 10, 29), now: now), isFalse);
      expect(
          AgeGate.isEligible(DateTime(2008, 2, 29), now: DateTime(2026, 2, 28)),
          isFalse);
      expect(
          AgeGate.isEligible(DateTime(2008, 2, 29), now: DateTime(2026, 3, 1)),
          isTrue);
    });

    test('refusal copy is neutral', () {
      for (final text in [AgeGate.refusalTitle, AgeGate.refusalMessage]) {
        expect(text, isNot(matches(RegExp(r'\d'))));
        expect(text.toLowerCase(), isNot(contains('yaş')));
        expect(text.toLowerCase(), isNot(contains('tekrar')));
      }
    });

    test('AuthUser reads requiresOnboarding from every API shape', () {
      expect(_googleUserNeedingOnboarding().requiresOnboarding, isTrue);
      // GET /api/v1/users/me: the profile itself.
      expect(
        AuthUser.fromJson({
          'id': 'u1',
          'username': 'k',
          'email': 'k@example.com',
          'requiresOnboarding': true,
        }).requiresOnboarding,
        isTrue,
      );
      // /auth/refresh and older APIs omit it.
      expect(
        AuthUser.fromJson(
                {'id': 'u1', 'username': 'k', 'email': 'k@example.com'})
            .requiresOnboarding,
        isFalse,
      );
    });

    test('redirect sends only accounts without a birth date to the step', () {
      expect(
        ageOnboardingRedirect(requiresOnboarding: true, location: '/'),
        ageOnboardingPath,
      );
      expect(
        ageOnboardingRedirect(requiresOnboarding: true, location: '/create'),
        ageOnboardingPath,
      );
      for (final open in [ageOnboardingPath, '/splash', '/legal/terms']) {
        expect(
          ageOnboardingRedirect(requiresOnboarding: true, location: open),
          isNull,
        );
      }
      expect(ageOnboardingRedirect(requiresOnboarding: false, location: '/'),
          isNull);
    });
  });

  group('RegisterScreen neutral gate', () {
    testWidgets('picker ends today, preselects nothing; no 18+ checkbox',
        (tester) async {
      await _useTallPhone(tester);
      await tester
          .pumpWidget(_app(RegisterScreen(authService: _FakeAuthService())));
      await tester.pumpAndSettle();

      // The threshold is never shown and the separate age checkbox is gone.
      expect(find.textContaining('18'), findsNothing);
      expect(find.textContaining('yaşında'), findsNothing);
      expect(find.byType(CheckboxListTile), findsOneWidget,
          reason: 'only the terms checkbox remains');

      await tester.tap(find.widgetWithText(TextFormField, 'Doğum Tarihi'));
      await tester.pumpAndSettle();
      final picker =
          tester.widget<DatePickerDialog>(find.byType(DatePickerDialog));
      expect(picker.lastDate, DateUtils.dateOnly(DateTime.now()));
      expect(picker.initialDate, isNull,
          reason: 'no default year leading toward an adult date');
    });

    testWidgets('under-18 shows the neutral refusal and persists the lock',
        (tester) async {
      await _useTallPhone(tester);
      final auth = _FakeAuthService();
      await tester.pumpWidget(_app(RegisterScreen(authService: auth)));
      await tester.pumpAndSettle();

      await tester.enterText(
          find.widgetWithText(TextFormField, 'Kullanıcı adı'), 'kararci_1');
      await tester.pump(const Duration(milliseconds: 600)); // availability
      await tester.enterText(
          find.widgetWithText(TextFormField, 'E-posta'), 'k@example.com');
      await _pickBirthDate(tester, _yearsAgo(14));
      await tester.tap(
          find.widgetWithText(DropdownButtonFormField<String>, 'Cinsiyet'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Kadın').last);
      await tester.pumpAndSettle();
      await tester.enterText(
          find.widgetWithText(TextFormField, 'Şifre'), 'Passw0rd!');
      await tester.enterText(
          find.widgetWithText(TextFormField, 'Şifre Tekrar'), 'Passw0rd!');
      await tester.tap(find.byType(CheckboxListTile));
      await tester.pump();

      await tester.tap(find.text('Kaydol'));
      await tester.pumpAndSettle();

      expect(find.text(AgeGate.refusalTitle), findsOneWidget);
      expect(find.text(AgeGate.refusalMessage), findsOneWidget);
      expect(find.text('Kaydol'), findsNothing);
      expect(find.byType(TextFormField), findsNothing);
      expect(await _storedLock(), isNotNull);
      expect(auth.registerCalls, 0,
          reason: 'no account data is sent for an ineligible date');
      expect(auth.declared, hasLength(1),
          reason: 'the device is recorded on the server (date only)');
    });

    testWidgets('a locked device stays locked on retry', (tester) async {
      FlutterSecureStorage.setMockInitialValues(
          {'age_gate_locked_at': '2026-10-01T00:00:00.000Z'});
      await _useTallPhone(tester);

      for (var attempt = 0; attempt < 2; attempt++) {
        // Re-entering the screen (back navigation, app restart) rebuilds it.
        await tester.pumpWidget(const SizedBox());
        await tester
            .pumpWidget(_app(RegisterScreen(authService: _FakeAuthService())));
        await tester.pumpAndSettle();

        expect(find.text(AgeGate.refusalTitle), findsOneWidget);
        expect(find.byType(TextFormField), findsNothing);
        expect(find.text('Google ile Kaydol'), findsNothing);
      }
    });
  });

  group('Google onboarding step', () {
    GoRouter router(_FakeAuthService auth) => GoRouter(
          initialLocation: '/',
          redirect: (_, state) => ageOnboardingRedirect(
            requiresOnboarding: auth.currentUser?.requiresOnboarding == true,
            location: state.matchedLocation,
          ),
          routes: [
            GoRoute(path: '/', builder: (_, __) => const Text('akış')),
            GoRoute(
              path: ageOnboardingPath,
              builder: (_, __) => AgeOnboardingScreen(authService: auth),
            ),
            GoRoute(
              path: '/legal/:doc',
              builder: (_, __) => const Text('legal'),
            ),
          ],
        );

    Future<void> pumpRouter(WidgetTester tester, _FakeAuthService auth) async {
      await _useTallPhone(tester);
      await tester.pumpWidget(MaterialApp.router(
        theme: AppTheme.light(),
        builder: _scaled,
        routerConfig: router(auth),
      ));
      await tester.pumpAndSettle();
    }

    testWidgets('requiresOnboarding routes to the birth-date step',
        (tester) async {
      final auth = _FakeAuthService(user: _googleUserNeedingOnboarding());
      await pumpRouter(tester, auth);

      expect(find.byType(AgeOnboardingScreen), findsOneWidget);
      expect(find.text('akış'), findsNothing);
      expect(find.textContaining('18'), findsNothing);

      await _pickBirthDate(tester, _yearsAgo(30));
      await tester.tap(find.byType(CheckboxListTile));
      await tester.pump();
      await tester.tap(find.text('Devam et'));
      await tester.pumpAndSettle();

      expect(auth.onboardingDates, hasLength(1));
      expect(find.text('akış'), findsOneWidget,
          reason: 'after the step the app opens normally');
    });

    testWidgets('an ineligible date ends the session and locks the device',
        (tester) async {
      final auth = _FakeAuthService(user: _googleUserNeedingOnboarding());
      await pumpRouter(tester, auth);

      await _pickBirthDate(tester, _yearsAgo(13));
      await tester.tap(find.byType(CheckboxListTile));
      await tester.pump();
      await tester.tap(find.text('Devam et'));
      await tester.pumpAndSettle();

      expect(find.text(AgeGate.refusalTitle), findsOneWidget);
      expect(auth.currentUser, isNull);
      expect(await _storedLock(), isNotNull);
    });

    testWidgets('a locked device cannot complete the step', (tester) async {
      FlutterSecureStorage.setMockInitialValues(
          {'age_gate_locked_at': '2026-10-01T00:00:00.000Z'});
      final auth = _FakeAuthService(user: _googleUserNeedingOnboarding());
      await pumpRouter(tester, auth);

      expect(find.text(AgeGate.refusalTitle), findsOneWidget);
      expect(find.text('Devam et'), findsNothing);
      expect(auth.logoutCalls, 1);
      expect(auth.onboardingDates, isEmpty);
    });
  });

  test('production router wires the redirect, the route and both wrappers', () {
    final router = File('lib/core/router/app_router.dart').readAsStringSync();
    expect(router, contains('redirect: (_, state) => ageOnboardingRedirect('));
    expect(router, contains('path: ageOnboardingPath'));
    expect(
      RegExp(r'if \(!await _completeAgeOnboarding\(context, (widget\.)?authService\)\) return;')
          .allMatches(router)
          .length,
      2,
      reason: 'Google sign-in from the login and the register screen',
    );
  });
}
