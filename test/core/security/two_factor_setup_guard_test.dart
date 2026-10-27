// Güvenlik regresyonu: 2FA etkin kullanıcıda `/settings/2fa-setup` açılınca
// TOTP sırrının sessizce yenisiyle değiştirilmesi (hesap kilitlenmesi).
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/api/api_client.dart';
import 'package:karar/core/api/api_exception.dart';
import 'package:karar/core/auth/auth_service.dart';
import 'package:karar/core/providers.dart';
import 'package:karar/core/storage/secure_storage.dart';
import 'package:karar/features/auth/two_factor_setup_screen.dart';

const _secret = 'JBSWY3DPEHPK3PXP';
const _otpAuthUrl =
    'otpauth://totp/Karar:ali?secret=$_secret&issuer=Karar&digits=6&period=30';

class _FakeStorage implements SecureStorage {
  @override
  Future<String?> readAccessToken() async => 'access-token';

  @override
  dynamic noSuchMethod(Invocation invocation) => Future<Null>.value();
}

/// Sunucuyu taklit eder; `is2faEnabled` değeri test sırasında değiştirilebilir
/// (ör. kullanıcı 2FA'yı başka bir cihazda açtı).
class _FakeBackend {
  _FakeBackend({required this.is2faEnabled});

  bool is2faEnabled;
  final List<String> calls = [];

  Dio dio() {
    final dio = Dio(BaseOptions(baseUrl: 'http://localhost'));
    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) {
          calls.add('${options.method} ${options.path}');
          final Object? body = switch ('${options.method} ${options.path}') {
            'GET /api/v1/users/me' => {
                'id': 'user-1',
                'username': 'ali',
                'email': 'ali@example.com',
                'karma': 0,
                'authProvider': 'password',
                'is2faEnabled': is2faEnabled,
              },
            'POST /api/v1/auth/2fa/setup' => {
                'secret': _secret,
                'otpAuthUrl': _otpAuthUrl,
              },
            _ => null,
          };
          handler.resolve(
            Response(
              requestOptions: options,
              statusCode: body == null ? 204 : 200,
              data: body,
            ),
          );
        },
      ),
    );
    return dio;
  }

  int get setupCalls =>
      calls.where((c) => c == 'POST /api/v1/auth/2fa/setup').length;
}

Future<AuthService> _signedInAuthService(_FakeBackend backend) async {
  final authService = AuthService(
    storage: _FakeStorage(),
    apiClient: ApiClient(dio: backend.dio()),
  );
  await authService.init();
  return authService;
}

Widget _screen(AuthService authService) => ProviderScope(
      overrides: [authServiceProvider.overrideWithValue(authService)],
      child: const MaterialApp(home: TwoFactorSetupScreen()),
    );

void main() {
  group('AuthService.setup2fa', () {
    test('2FA etkinse mevcut TOTP sırrını değiştiren istek gönderilmez',
        () async {
      final backend = _FakeBackend(is2faEnabled: true);
      final authService = await _signedInAuthService(backend);
      expect(authService.currentUser?.is2faEnabled, isTrue);

      await expectLater(
        authService.setup2fa(),
        throwsA(
          isA<ApiException>()
              .having((e) => e.code, 'code', 'TWO_FACTOR_ALREADY_ENABLED')
              .having((e) => e.statusCode, 'statusCode', 409),
        ),
      );
      expect(backend.setupCalls, 0);
    });

    test('yerel durum eskiyse (2FA başka cihazda açıldı) yine reddedilir',
        () async {
      final backend = _FakeBackend(is2faEnabled: false);
      final authService = await _signedInAuthService(backend);
      expect(authService.currentUser?.is2faEnabled, isFalse);

      backend.is2faEnabled = true;

      await expectLater(authService.setup2fa(), throwsA(isA<ApiException>()));
      expect(backend.setupCalls, 0);
      expect(authService.currentUser?.is2faEnabled, isTrue);
    });

    test('2FA kapalıysa kurulum sırrı alınır', () async {
      final backend = _FakeBackend(is2faEnabled: false);
      final authService = await _signedInAuthService(backend);

      final data = await authService.setup2fa();

      expect(backend.setupCalls, 1);
      expect(data['secret'], _secret);
      expect(data['otpAuthUrl'], _otpAuthUrl);
    });
  });

  group('TwoFactorSetupScreen', () {
    testWidgets('2FA etkin kullanıcıda ekran açılınca sır yenilenmez',
        (tester) async {
      final backend = _FakeBackend(is2faEnabled: true);
      final authService =
          (await tester.runAsync(() => _signedInAuthService(backend)))!;

      await tester.pumpWidget(_screen(authService));
      await tester.pumpAndSettle();

      expect(backend.setupCalls, 0);
      expect(find.text(_secret), findsNothing);
      expect(find.text('İki faktörlü doğrulama zaten etkin.'), findsOneWidget);
    });

    testWidgets('2FA kapalı kullanıcıya API yanıtındaki sır gösterilir',
        (tester) async {
      final backend = _FakeBackend(is2faEnabled: false);
      final authService =
          (await tester.runAsync(() => _signedInAuthService(backend)))!;

      await tester.pumpWidget(_screen(authService));
      await tester.pumpAndSettle();

      expect(backend.setupCalls, 1);
      // API sözleşmesi: TwoFactorSetupResponse(secret, otpAuthUrl)
      expect(find.text(_secret), findsOneWidget);
      // Sır hiçbir zaman ağdan (üçüncü taraf QR servisi) yüklenen bir
      // görsele gömülmez.
      expect(find.byType(Image), findsNothing);
    });
  });
}
