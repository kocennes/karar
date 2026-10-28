import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:karar/core/api/api_client.dart';
import 'package:karar/core/api/api_endpoints.dart';
import 'package:karar/core/api/api_exception.dart';
import 'package:karar/core/auth/age_gate.dart';
import 'package:karar/core/auth/age_gate_coordinator.dart';
import 'package:karar/core/auth/auth_service.dart';
import 'package:karar/core/storage/secure_storage.dart';
import 'package:karar/core/theme/app_theme.dart';
import 'package:karar/features/auth/age_gate_ui.dart';
import 'package:karar/features/auth/age_onboarding_screen.dart';
import 'package:karar/features/auth/login_screen.dart';
import 'package:karar/features/auth/register_screen.dart';
import 'package:karar/features/auth/widgets/age_gate_widgets.dart';
import 'package:karar/features/feed/post_repository.dart';
import 'package:karar/shared/models/post.dart';

// ── Mocked HTTP layer ────────────────────────────────────────────────────────
//
// Everything below the network is real: ApiClient (incl. its age-gate hook),
// AuthService, AgeGateCoordinator, SecureStorage (over the mocked platform
// store), the screens and the router binding. Only Dio is answered locally,
// like the repository tests do, and the Google/Firebase SDK is a fake bridge.

class _Reply {
  const _Reply(this.status, this.body);
  final int status;
  final Object? body;
}

class _Backend {
  _Backend() {
    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) {
          requests.add(options);
          final queue = _replies['${options.method} ${options.path}'];
          final reply = queue == null || queue.isEmpty
              ? const _Reply(200, <String, Object?>{})
              : (queue.length > 1 ? queue.removeAt(0) : queue.first);
          handler.resolve(
            Response(
              requestOptions: options,
              statusCode: reply.status,
              data: reply.status == 204 ? null : reply.body,
            ),
          );
        },
      ),
    );
  }

  final dio = Dio(BaseOptions(baseUrl: 'http://localhost'));
  final requests = <RequestOptions>[];
  final _replies = <String, List<_Reply>>{};

  /// Queues a reply; replies are used in order and the last one sticks.
  void on(
    String method,
    String path,
    int status, [
    Object? body = const <String, Object?>{},
  ]) =>
      (_replies['$method $path'] ??= []).add(_Reply(status, body));

  void refuse(String method, String path, String code) =>
      on(method, path, 403, {
        'error': {'code': code, 'message': 'server text'},
      });

  List<RequestOptions> calls(String method, String path) => requests
      .where((r) => r.method == method && r.path == path)
      .toList(growable: false);
}

/// Stands in for the Google account picker + Firebase.
class _FakeGoogle extends GoogleAuthBridge {
  int idTokenCalls = 0;
  int signOutCalls = 0;

  @override
  Future<String> idToken() async {
    idTokenCalls++;
    return 'google-id-token';
  }

  @override
  Future<void> signOut() async => signOutCalls++;
}

class _RecordingUi {
  int refusals = 0;
  int onboardings = 0;

  AgeGateUi get ui => AgeGateUi(
        showRefusal: () => refusals++,
        showOnboarding: () => onboardings++,
        askDeclaration: (_) async => false,
      );
}

/// The production wiring of AppServices, over the mocked backend.
class _Stack {
  _Stack() {
    api = ApiClient(dio: backend.dio, accessTokenReader: storage.readAccessToken);
    auth = AuthService(storage: storage, apiClient: api, google: google);
    ageGate = AgeGateCoordinator(authService: auth);
    api.setAgeGateHooks(ageGate);
    ageGate.bindUi(recorded.ui);
  }

  final backend = _Backend();
  final storage = SecureStorage();
  final google = _FakeGoogle();
  final recorded = _RecordingUi();
  late final ApiClient api;
  late final AuthService auth;
  late final AgeGateCoordinator ageGate;

  PostRepository get posts => PostRepository(apiClient: api);

  /// A real sign-in through `POST /auth/google`.
  Future<void> signInWithGoogle({required bool requiresOnboarding}) async {
    backend.on('POST', ApiEndpoints.authGoogle, 200,
        _session(requiresOnboarding: requiresOnboarding));
    await auth.loginWithGoogle();
    backend.requests.clear();
  }
}

Map<String, Object?> _session({required bool requiresOnboarding}) => {
      // Shape of the 201 body of POST /api/v1/auth/google.
      'userId': 'u1',
      'username': 'kararci',
      'accessToken': 'access',
      'refreshToken': 'refresh',
      'isNewUser': false,
      'requiresOnboarding': requiresOnboarding,
      'user': {
        'id': 'u1',
        'username': 'kararci',
        'email': 'k@example.com',
        'karma': 0,
        'authProvider': 'google',
        'emailVerified': true,
      },
    };

const _postJson = <String, Object?>{
  'id': 'p1',
  'title': 'Arkadaşım borç istedi, vermedim',
  'imageUrl': null,
  'category': {'id': 1, 'name': 'İş Hayatı', 'emoji': 'İş'},
  'voteCountHakli': 1,
  'voteCountHaksiz': 0,
  'commentCount': 0,
  'myVote': 'hakli',
  'createdAt': '2026-10-01T10:00:00Z',
  'isOwner': false,
};

const _votePath = '/api/v1/posts/p1/vote';

/// The outcome of a request: its value or the error it threw.
Future<Object?> _settle(Future<Object?> request) =>
    request.then<Object?>((value) => value, onError: (Object e) => e);

/// Starts a request inside a widget test. Dio runs on zero-delay timers,
/// which fire only while the tester pumps, so the request is driven by
/// pumping (and any UI it opens can be used) before its outcome is awaited.
Future<Future<Object?>> _start(
  WidgetTester tester,
  Future<Object?> request,
) async {
  final outcome = _settle(request);
  await tester.pumpAndSettle();
  return outcome;
}

// ── UI helpers ───────────────────────────────────────────────────────────────

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
  final today = AgeGate.today();
  return DateTime(today.year - years, today.month, today.day);
}

String _usDate(DateTime d) =>
    '${d.month.toString().padLeft(2, '0')}/${d.day.toString().padLeft(2, '0')}/${d.year}';

String _isoDate(DateTime d) => DateTime(d.year, d.month, d.day).toIso8601String();

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

Future<void> _fillRegisterForm(WidgetTester tester, DateTime dob) async {
  await tester.enterText(
      find.widgetWithText(TextFormField, 'Kullanıcı adı'), 'kararci_1');
  await tester.pump(const Duration(milliseconds: 600)); // availability
  await tester.enterText(
      find.widgetWithText(TextFormField, 'E-posta'), 'k@example.com');
  await _pickBirthDate(tester, dob);
  await tester
      .tap(find.widgetWithText(DropdownButtonFormField<String>, 'Cinsiyet'));
  await tester.pumpAndSettle();
  await tester.tap(find.text('Kadın').last);
  await tester.pumpAndSettle();
  await tester.enterText(
      find.widgetWithText(TextFormField, 'Şifre'), 'Passw0rd!');
  await tester.enterText(
      find.widgetWithText(TextFormField, 'Şifre Tekrar'), 'Passw0rd!');
  await tester.tap(find.byType(CheckboxListTile));
  await tester.pump();
}

Future<void> _submitOnboarding(WidgetTester tester, DateTime dob) async {
  await _pickBirthDate(tester, dob);
  await tester.tap(find.byType(CheckboxListTile));
  await tester.pump();
  await tester.tap(find.text('Devam et'));
  await tester.pumpAndSettle();
}

GoRouter _router(AuthService auth) => GoRouter(
      initialLocation: '/',
      redirect: (_, state) => ageOnboardingRedirect(
        requiresOnboarding: auth.currentUser?.requiresOnboarding == true,
        location: state.matchedLocation,
      ),
      routes: [
        GoRoute(
          path: '/',
          builder: (_, __) => const Scaffold(body: Text('akış')),
        ),
        GoRoute(
          path: ageOnboardingPath,
          builder: (_, __) => AgeOnboardingScreen(authService: auth),
        ),
        GoRoute(
          path: ageRefusalPath,
          builder: (_, __) => const AgeRefusalScreen(),
        ),
        GoRoute(
          path: '/legal/:doc',
          builder: (_, __) => const Text('legal'),
        ),
      ],
    );

/// The app shell: the production router binding of the coordinator.
Future<GoRouter> _pumpRouter(WidgetTester tester, _Stack stack) async {
  await _useTallPhone(tester);
  final router = _router(stack.auth);
  stack.ageGate.bindUi(routerAgeGateUi(router));
  await tester.pumpWidget(MaterialApp.router(
    theme: AppTheme.light(),
    builder: _scaled,
    routerConfig: router,
  ));
  await tester.pumpAndSettle();
  return router;
}

Future<String?> _storedLock() => SecureStorage().readAgeGateLock();

Future<bool> _hasTokens() async {
  final storage = SecureStorage();
  return await storage.readAccessToken() != null ||
      await storage.readRefreshToken() != null;
}

void _lockDevice() => FlutterSecureStorage.setMockInitialValues(
    {'age_gate_locked_at': '2026-10-01T00:00:00.000Z'});

// ── Tests ────────────────────────────────────────────────────────────────────

void main() {
  setUp(() => FlutterSecureStorage.setMockInitialValues({}));

  group('AgeGate (pure)', () {
    test('eligibility turns on exactly at the birthday', () {
      final noonInTurkey = DateTime.utc(2026, 10, 28, 9);
      expect(AgeGate.isEligible(DateTime(2008, 10, 28), now: noonInTurkey),
          isTrue);
      expect(AgeGate.isEligible(DateTime(2008, 10, 29), now: noonInTurkey),
          isFalse);
      expect(
          AgeGate.isEligible(DateTime(2008, 2, 29),
              now: DateTime.utc(2026, 2, 28, 9)),
          isFalse);
      expect(
          AgeGate.isEligible(DateTime(2008, 2, 29),
              now: DateTime.utc(2026, 3, 1, 9)),
          isTrue);
    });

    test('"today" is the Turkish calendar date (UTC+3), not the device date',
        () {
      // 21:30 UTC on the 27th is 00:30 on the 28th in Turkey.
      final lateEvening = DateTime.utc(2026, 10, 27, 21, 30);
      expect(AgeGate.today(lateEvening), DateTime(2026, 10, 28));
      expect(AgeGate.isEligible(DateTime(2008, 10, 28), now: lateEvening),
          isTrue,
          reason: 'already the birthday in Turkey');

      // 20:59 UTC is 23:59 on the 27th in Turkey: not yet.
      final beforeMidnight = DateTime.utc(2026, 10, 27, 20, 59);
      expect(AgeGate.today(beforeMidnight), DateTime(2026, 10, 27));
      expect(AgeGate.isEligible(DateTime(2008, 10, 28), now: beforeMidnight),
          isFalse);

      // The same instant read as a device-local time gives the same date.
      expect(AgeGate.today(lateEvening.toLocal()), DateTime(2026, 10, 28));
      expect(AgeGate.today(DateTime.utc(2026, 10, 28, 20, 59)),
          DateTime(2026, 10, 28));
    });

    test('refusal copy is neutral', () {
      for (final text in [
        AgeGate.refusalTitle,
        AgeGate.refusalMessage,
        AgeGate.birthDatePrompt,
      ]) {
        expect(text, isNot(matches(RegExp(r'\d'))));
        expect(text.toLowerCase(), isNot(contains('yaş')));
        expect(text.toLowerCase(), isNot(contains('tekrar')));
      }
      expect(AgeGate.refusalTitle, isNot(contains('Kayıt')),
          reason: 'also shown to existing accounts');
      expect(AgeGate.refusalMessage, "Üzgünüz, Karar'ı kullanamıyorsun.");
    });

    test('age-gate errors never show the generic 403 text', () {
      ApiException error(String code) =>
          ApiException(statusCode: 403, code: code, message: 'server text');
      expect(error(AgeGate.notEligibleCode).friendlyMessage,
          AgeGate.refusalMessage);
      expect(error(AgeGate.verificationRequiredCode).friendlyMessage,
          AgeGate.birthDatePrompt);
      expect(error(AgeGate.declarationRequiredCode).friendlyMessage,
          AgeGate.birthDatePrompt);
      expect(error('FORBIDDEN').friendlyMessage, 'Bu işlem için yetkin yok.');
    });

    test('AuthUser reads requiresOnboarding from every API shape', () {
      expect(AuthUser.fromJson(_session(requiresOnboarding: true))
              .requiresOnboarding,
          isTrue);
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

    test('a response without the key keeps the same account\'s flag', () {
      final before = AuthUser.fromJson(_session(requiresOnboarding: true));
      const profile = {'id': 'u1', 'username': 'k', 'email': 'k@example.com'};
      expect(AuthUser.fromJson(profile, previous: before).requiresOnboarding,
          isTrue);
      expect(
        AuthUser.fromJson({...profile, 'requiresOnboarding': false},
                previous: before)
            .requiresOnboarding,
        isFalse,
        reason: 'an explicit value wins',
      );
      expect(
        AuthUser.fromJson({...profile, 'id': 'u2'}, previous: before)
            .requiresOnboarding,
        isFalse,
        reason: 'never carried over to another account',
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
      for (final open in [
        ageOnboardingPath,
        ageRefusalPath,
        '/splash',
        '/legal/terms',
      ]) {
        expect(
          ageOnboardingRedirect(requiresOnboarding: true, location: open),
          isNull,
        );
      }
      expect(ageOnboardingRedirect(requiresOnboarding: false, location: '/'),
          isNull);
    });
  });

  group('ApiClient age-gate hook', () {
    test('a declaration is retried at most once', () async {
      final backend = _Backend()
        ..refuse('POST', _votePath, AgeGate.declarationRequiredCode);
      final hooks = _CountingHooks(declare: true);
      final api = ApiClient(dio: backend.dio)..setAgeGateHooks(hooks);

      final error = await _settle(api.postJson<void>(_votePath));

      expect(error, isA<ApiException>().having((e) => e.code, 'code',
          AgeGate.declarationRequiredCode));
      expect(hooks.declarations, 1);
      expect(backend.calls('POST', _votePath), hasLength(2),
          reason: 'the original request and one retry, no loop');
    });

    test('each code reaches its hook; other errors do not', () async {
      final backend = _Backend()
        ..refuse('POST', '/a', AgeGate.notEligibleCode)
        ..refuse('POST', '/b', AgeGate.verificationRequiredCode)
        ..refuse('POST', '/c', 'FORBIDDEN')
        ..on('POST', '/d', 400, {
          'error': {'code': AgeGate.notEligibleCode, 'message': ''},
        });
      final hooks = _CountingHooks(declare: false);
      final api = ApiClient(dio: backend.dio)..setAgeGateHooks(hooks);

      for (final path in ['/a', '/b', '/c', '/d']) {
        await _settle(api.postJson<void>(path));
      }

      expect(hooks.notEligible, 1);
      expect(hooks.verifications, 1);
      expect(hooks.declarations, 0);
    });
  });

  group('server AGE_NOT_ELIGIBLE (real AuthService over mocked HTTP)', () {
    testWidgets('on register: neutral refusal, device locked', (tester) async {
      await _useTallPhone(tester);
      final stack = _Stack();
      stack.backend
          .refuse('POST', ApiEndpoints.authRegister, AgeGate.notEligibleCode);
      await tester.pumpWidget(_app(RegisterScreen(authService: stack.auth)));
      await tester.pumpAndSettle();

      // An eligible date by the local check: the server refuses anyway
      // (e.g. this device or e-mail was refused before).
      await _fillRegisterForm(tester, _yearsAgo(30));
      await tester.tap(find.text('Kaydol'));
      await tester.pumpAndSettle();

      expect(stack.backend.calls('POST', ApiEndpoints.authRegister),
          hasLength(1));
      expect(find.text(AgeGate.refusalTitle), findsOneWidget);
      expect(find.text(AgeGate.refusalMessage), findsOneWidget);
      expect(find.byType(TextFormField), findsNothing);
      expect(await _storedLock(), isNotNull);
      expect(stack.auth.isLoggedIn, isFalse);
      expect(stack.recorded.refusals, 1,
          reason: 'answered by the central handler');
    });

    testWidgets('on Google sign-in: refusal, device locked, no session',
        (tester) async {
      await _useTallPhone(tester);
      final stack = _Stack();
      stack.backend
          .refuse('POST', ApiEndpoints.authGoogle, AgeGate.notEligibleCode);
      var signedIn = 0;
      await tester.pumpWidget(_app(LoginScreen(
        authService: stack.auth,
        onSuccess: () => signedIn++,
      )));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Google ile Devam Et'));
      await tester.pumpAndSettle();

      expect(stack.backend.calls('POST', ApiEndpoints.authGoogle),
          hasLength(1));
      expect(find.text(AgeGate.refusalTitle), findsOneWidget);
      expect(find.text(AgeGate.refusalMessage), findsOneWidget);
      expect(find.text('Giriş Yap'), findsNothing);
      expect(await _storedLock(), isNotNull);
      expect(stack.auth.isLoggedIn, isFalse);
      expect(await _hasTokens(), isFalse);
      expect(stack.google.signOutCalls, greaterThan(0),
          reason: 'the Google/Firebase sign-in does not outlive the refusal');
      expect(signedIn, 0);
    });

    testWidgets('on password sign-in of a refused account: refusal',
        (tester) async {
      await _useTallPhone(tester);
      final stack = _Stack();
      stack.backend
          .refuse('POST', ApiEndpoints.authLogin, AgeGate.notEligibleCode);
      await tester.pumpWidget(_app(LoginScreen(authService: stack.auth)));
      await tester.pumpAndSettle();

      await tester.enterText(
          find.widgetWithText(TextFormField, 'Kullanıcı adı veya e-posta'),
          'kararci');
      await tester.enterText(
          find.widgetWithText(TextFormField, 'Şifre'), 'Passw0rd!');
      await tester.tap(find.text('Giriş Yap'));
      await tester.pumpAndSettle();

      expect(find.text(AgeGate.refusalMessage), findsOneWidget);
      expect(await _storedLock(), isNotNull);
      expect(await _hasTokens(), isFalse);
    });
  });

  group('locked device', () {
    testWidgets('login screen: Google sign-in never reaches Google or the API',
        (tester) async {
      _lockDevice();
      await _useTallPhone(tester);
      final stack = _Stack();
      await tester.pumpWidget(_app(LoginScreen(authService: stack.auth)));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Google ile Devam Et'));
      await tester.pumpAndSettle();

      expect(stack.google.idTokenCalls, 0);
      expect(stack.backend.requests, isEmpty);
      expect(find.text(AgeGate.refusalTitle), findsOneWidget);
      expect(find.text(AgeGate.refusalMessage), findsOneWidget);
    });

    test('every Google entry point is covered by the service check', () async {
      _lockDevice();
      final stack = _Stack();
      final error = await _settle(stack.auth.loginWithGoogle());
      expect(error, isA<ApiException>()
          .having((e) => e.code, 'code', AgeGate.notEligibleCode));
      expect(stack.google.idTokenCalls, 0);
      expect(stack.backend.requests, isEmpty);
    });

    testWidgets('register screen stays closed on every visit', (tester) async {
      _lockDevice();
      await _useTallPhone(tester);

      for (var attempt = 0; attempt < 2; attempt++) {
        // Re-entering the screen (back navigation, app restart) rebuilds it.
        await tester.pumpWidget(const SizedBox());
        await tester
            .pumpWidget(_app(RegisterScreen(authService: _Stack().auth)));
        await tester.pumpAndSettle();

        expect(find.text(AgeGate.refusalTitle), findsOneWidget);
        expect(find.byType(TextFormField), findsNothing);
        expect(find.text('Google ile Kaydol'), findsNothing);
      }
    });
  });

  group('RegisterScreen neutral gate', () {
    testWidgets('picker ends today (Turkish date), preselects nothing',
        (tester) async {
      await _useTallPhone(tester);
      await tester
          .pumpWidget(_app(RegisterScreen(authService: _Stack().auth)));
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
      expect(picker.lastDate, AgeGate.today());
      expect(picker.initialDate, isNull,
          reason: 'no default year leading toward an adult date');
    });

    testWidgets('an ineligible date is refused locally and recorded',
        (tester) async {
      await _useTallPhone(tester);
      final stack = _Stack();
      stack.backend.refuse(
          'POST', ApiEndpoints.deviceAgeDeclaration, AgeGate.notEligibleCode);
      await tester.pumpWidget(_app(RegisterScreen(authService: stack.auth)));
      await tester.pumpAndSettle();

      final dob = _yearsAgo(14);
      await _fillRegisterForm(tester, dob);
      await tester.tap(find.text('Kaydol'));
      await tester.pumpAndSettle();

      expect(find.text(AgeGate.refusalTitle), findsOneWidget);
      expect(find.text(AgeGate.refusalMessage), findsOneWidget);
      expect(find.text('Kaydol'), findsNothing);
      expect(find.byType(TextFormField), findsNothing);
      expect(await _storedLock(), isNotNull);
      expect(stack.backend.calls('POST', ApiEndpoints.authRegister), isEmpty,
          reason: 'no account data is sent for an ineligible date');
      final declared =
          stack.backend.calls('POST', ApiEndpoints.deviceAgeDeclaration);
      expect(declared, hasLength(1),
          reason: 'the device is recorded on the server (date only)');
      expect(declared.single.data, {'dateOfBirth': _isoDate(dob)});
    });
  });

  group('birth-date step (onboarding)', () {
    testWidgets('requiresOnboarding routes to the step; no "Kapat"',
        (tester) async {
      final stack = _Stack();
      await tester.runAsync(
          () => stack.signInWithGoogle(requiresOnboarding: true));
      await _pumpRouter(tester, stack);

      expect(find.byType(AgeOnboardingScreen), findsOneWidget);
      expect(find.text('akış'), findsNothing);
      expect(find.textContaining('18'), findsNothing);
      expect(find.byTooltip('Kapat'), findsNothing,
          reason: 'it would only bounce back here through the redirect');
      expect(find.byType(BackButton), findsNothing);
      expect(find.text('Vazgeç ve çıkış yap'), findsOneWidget);
    });

    testWidgets('server 200 proceeds even where the local check disagrees',
        (tester) async {
      final stack = _Stack();
      await tester.runAsync(
          () => stack.signInWithGoogle(requiresOnboarding: true));
      stack.backend.on('POST', ApiEndpoints.userMeOnboarding, 200,
          {'requiresOnboarding': false});
      await _pumpRouter(tester, stack);

      // The 18th birthday is tomorrow by the Turkish date; around midnight a
      // device in another time zone (or a skewed clock) can still see it
      // differently, and the server is the one that decides.
      final today = AgeGate.today();
      final dob = DateTime(today.year - 18, today.month, today.day + 1);
      expect(AgeGate.isEligible(dob), isFalse, reason: 'test precondition');

      await _submitOnboarding(tester, dob);

      final sent = stack.backend.calls('POST', ApiEndpoints.userMeOnboarding);
      expect(sent, hasLength(1));
      expect((sent.single.data as Map)['dateOfBirth'], _isoDate(dob));
      expect(find.text('akış'), findsOneWidget,
          reason: 'after the step the app opens normally');
      expect(find.text(AgeGate.refusalTitle), findsNothing);
      expect(await _storedLock(), isNull);
      expect(stack.auth.isLoggedIn, isTrue);
      expect(stack.auth.currentUser!.requiresOnboarding, isFalse);
    });

    testWidgets('server 403: refusal in place, device locked, session ended',
        (tester) async {
      final stack = _Stack();
      await tester.runAsync(
          () => stack.signInWithGoogle(requiresOnboarding: true));
      stack.backend.refuse(
          'POST', ApiEndpoints.userMeOnboarding, AgeGate.notEligibleCode);
      final router = await _pumpRouter(tester, stack);

      await _submitOnboarding(tester, _yearsAgo(13));

      expect(find.text(AgeGate.refusalTitle), findsOneWidget);
      expect(find.text(AgeGate.refusalMessage), findsOneWidget);
      expect(router.state.matchedLocation, ageOnboardingPath,
          reason: 'the step shows the refusal itself');
      expect(stack.auth.currentUser, isNull);
      expect(await _hasTokens(), isFalse);
      expect(await _storedLock(), isNotNull);
      expect(stack.backend.calls('POST', ApiEndpoints.authLogout),
          hasLength(1));
    });

    testWidgets('a locked device cannot complete the step', (tester) async {
      final stack = _Stack();
      await tester.runAsync(
          () => stack.signInWithGoogle(requiresOnboarding: true));
      await stack.auth.lockAgeGate();
      await _pumpRouter(tester, stack);

      expect(find.text(AgeGate.refusalTitle), findsOneWidget);
      expect(find.text('Devam et'), findsNothing);
      expect(stack.auth.isLoggedIn, isFalse);
      expect(await _hasTokens(), isFalse);
      expect(stack.backend.calls('POST', ApiEndpoints.userMeOnboarding),
          isEmpty);
    });

    test('PUT /users/me without the key keeps requiresOnboarding', () async {
      final stack = _Stack();
      await stack.signInWithGoogle(requiresOnboarding: true);
      stack.backend.on('PUT', ApiEndpoints.userMe, 200, {
        'id': 'u1',
        'username': 'yeni_ad',
        'email': 'k@example.com',
        'bio': 'merhaba',
      });

      await stack.auth.updateProfile(bio: 'merhaba');
      expect(stack.auth.currentUser!.bio, 'merhaba');
      expect(stack.auth.currentUser!.requiresOnboarding, isTrue);

      await stack.auth.changeUsername('yeni_ad');
      expect(stack.auth.currentUser!.username, 'yeni_ad');
      expect(stack.auth.currentUser!.requiresOnboarding, isTrue);
    });
  });

  group('writes under AgeGate:Enforce', () {
    testWidgets('AGE_VERIFICATION_REQUIRED opens the step; retry works after',
        (tester) async {
      final stack = _Stack();
      await tester.runAsync(
          () => stack.signInWithGoogle(requiresOnboarding: false));
      stack.backend
        ..refuse('POST', _votePath, AgeGate.verificationRequiredCode)
        ..on('POST', _votePath, 200)
        ..on('GET', '/api/v1/posts/p1', 200, _postJson)
        ..on('POST', ApiEndpoints.userMeOnboarding, 200,
            {'requiresOnboarding': false});
      final router = await _pumpRouter(tester, stack);
      expect(find.text('akış'), findsOneWidget);

      final error = await await _start(
          tester, stack.posts.vote('p1', VoteType.hakli));
      expect(error, isA<ApiException>().having(
          (e) => e.code, 'code', AgeGate.verificationRequiredCode));
      expect((error as ApiException).friendlyMessage, AgeGate.birthDatePrompt,
          reason: 'not the generic "Bu işlem için yetkin yok."');
      expect(router.state.matchedLocation, ageOnboardingPath);
      expect(find.byType(AgeOnboardingScreen), findsOneWidget);
      expect(stack.auth.currentUser!.requiresOnboarding, isTrue);

      await _submitOnboarding(tester, _yearsAgo(30));
      expect(find.text('akış'), findsOneWidget,
          reason: 'back where the user was');
      expect(stack.auth.currentUser!.requiresOnboarding, isFalse);

      final retried = await await _start(
          tester, stack.posts.vote('p1', VoteType.hakli));
      expect(retried, isA<Post>());
    });

    testWidgets('AGE_NOT_ELIGIBLE on a write: refusal page, lock, no session',
        (tester) async {
      final stack = _Stack();
      await tester.runAsync(
          () => stack.signInWithGoogle(requiresOnboarding: false));
      stack.backend.refuse('POST', _votePath, AgeGate.notEligibleCode);
      final router = await _pumpRouter(tester, stack);

      final error = await await _start(
          tester, stack.posts.vote('p1', VoteType.hakli));

      expect((error as ApiException).friendlyMessage, AgeGate.refusalMessage);
      expect(router.state.matchedLocation, ageRefusalPath);
      expect(find.text(AgeGate.refusalTitle), findsOneWidget);
      expect(find.text(AgeGate.refusalMessage), findsOneWidget);
      expect(stack.auth.isLoggedIn, isFalse);
      expect(await _hasTokens(), isFalse);
      expect(await _storedLock(), isNotNull);
    });
  });

  group('guest vote under AgeGate:GuestVote', () {
    Future<Future<Object?>> startVote(WidgetTester tester, _Stack stack) =>
        _start(tester, stack.posts.vote('p1', VoteType.hakli));

    testWidgets('declaration sheet → 204 → the vote is retried once',
        (tester) async {
      final stack = _Stack();
      stack.backend
        ..refuse('POST', _votePath, AgeGate.declarationRequiredCode)
        ..on('POST', _votePath, 200)
        ..on('GET', '/api/v1/posts/p1', 200, _postJson)
        ..on('POST', ApiEndpoints.deviceAgeDeclaration, 204);
      await _pumpRouter(tester, stack);

      final vote = await startVote(tester, stack);

      // The neutral sheet: same picker, nothing preselected, no threshold.
      expect(find.text(AgeGate.birthDatePrompt), findsOneWidget);
      expect(find.textContaining('18'), findsNothing);
      await tester.tap(find.widgetWithText(TextFormField, 'Doğum Tarihi'));
      await tester.pumpAndSettle();
      final picker =
          tester.widget<DatePickerDialog>(find.byType(DatePickerDialog));
      expect(picker.lastDate, AgeGate.today());
      expect(picker.initialDate, isNull);
      await tester.tap(find.text('Cancel'));
      await tester.pumpAndSettle();

      final dob = _yearsAgo(30);
      await _pickBirthDate(tester, dob);
      await tester.tap(find.text('Devam et'));
      await tester.pumpAndSettle();

      expect(await vote, isA<Post>().having((p) => p.id, 'id', 'p1'));
      expect(stack.backend.calls('POST', _votePath), hasLength(2),
          reason: 'the original vote and exactly one retry');
      final declared =
          stack.backend.calls('POST', ApiEndpoints.deviceAgeDeclaration);
      expect(declared, hasLength(1));
      expect(declared.single.data, {'dateOfBirth': _isoDate(dob)});
      expect(find.text(AgeGate.birthDatePrompt), findsNothing);
      expect(await _storedLock(), isNull);
    });

    testWidgets('declaration refused (403): refusal page and lock',
        (tester) async {
      final stack = _Stack();
      stack.backend
        ..refuse('POST', _votePath, AgeGate.declarationRequiredCode)
        ..refuse('POST', ApiEndpoints.deviceAgeDeclaration,
            AgeGate.notEligibleCode);
      final router = await _pumpRouter(tester, stack);

      final vote = await startVote(tester, stack);
      await _pickBirthDate(tester, _yearsAgo(12));
      await tester.tap(find.text('Devam et'));
      await tester.pumpAndSettle();

      final error = await vote;
      expect(error, isA<ApiException>()
          .having((e) => e.code, 'code', AgeGate.notEligibleCode));
      expect((error as ApiException).friendlyMessage, AgeGate.refusalMessage);
      expect(stack.backend.calls('POST', _votePath), hasLength(1),
          reason: 'never retried after a refusal');
      expect(await _storedLock(), isNotNull);
      expect(router.state.matchedLocation, ageRefusalPath);
      expect(find.text(AgeGate.refusalTitle), findsOneWidget);
      expect(find.text(AgeGate.refusalMessage), findsOneWidget);
      expect(find.text(AgeGate.birthDatePrompt), findsNothing);
    });

    testWidgets('dismissing the sheet keeps the vote unsent, no generic error',
        (tester) async {
      final stack = _Stack();
      stack.backend
          .refuse('POST', _votePath, AgeGate.declarationRequiredCode);
      await _pumpRouter(tester, stack);

      final vote = await startVote(tester, stack);
      await tester.tap(find.byTooltip('Kapat'));
      await tester.pumpAndSettle();

      final error = await vote;
      expect((error as ApiException).friendlyMessage, AgeGate.birthDatePrompt);
      expect(stack.backend.calls('POST', _votePath), hasLength(1));
      expect(
          stack.backend.calls('POST', ApiEndpoints.deviceAgeDeclaration),
          isEmpty);
      expect(await _storedLock(), isNull);
    });

    testWidgets('a locked device is not asked again', (tester) async {
      _lockDevice();
      final stack = _Stack();
      stack.backend
          .refuse('POST', _votePath, AgeGate.declarationRequiredCode);
      await _pumpRouter(tester, stack);

      final vote = await startVote(tester, stack);

      expect(await vote, isA<ApiException>()
          .having((e) => e.code, 'code', AgeGate.notEligibleCode));
      expect(find.text(AgeGate.birthDatePrompt), findsNothing);
      expect(
          stack.backend.calls('POST', ApiEndpoints.deviceAgeDeclaration),
          isEmpty);
    });
  });

  test('production wiring: hook, UI binding, routes and both wrappers', () {
    final router = File('lib/core/router/app_router.dart').readAsStringSync();
    expect(router, contains('redirect: (_, state) => ageOnboardingRedirect('));
    expect(router, contains('path: ageOnboardingPath'));
    expect(router, contains('path: ageRefusalPath'));
    expect(
      RegExp(r'if \(!await _completeAgeOnboarding\(context, (widget\.)?authService\)\) return;')
          .allMatches(router)
          .length,
      2,
      reason: 'Google sign-in from the login and the register screen',
    );

    final services = File('lib/core/app_services.dart').readAsStringSync();
    expect(services, contains('apiClient.setAgeGateHooks(ageGate)'));
    final app = File('lib/app.dart').readAsStringSync();
    expect(app, contains('ageGate.bindUi(routerAgeGateUi(_router))'));
  });
}

class _CountingHooks implements AgeGateHooks {
  _CountingHooks({required this.declare});

  final bool declare;
  int notEligible = 0;
  int verifications = 0;
  int declarations = 0;

  @override
  Future<void> onNotEligible() async => notEligible++;

  @override
  Future<void> onVerificationRequired() async => verifications++;

  @override
  Future<bool> onDeclarationRequired() async {
    declarations++;
    return declare;
  }
}
