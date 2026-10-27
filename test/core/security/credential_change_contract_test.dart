// Sözleşme regresyonu: sunucu 2FA kapatmada şifre + ikinci faktör ister ve
// şifre / e-posta değişince, gönderilen refresh token dışındaki tüm oturumları
// kapatır. İstemci bunları göndermezse 2FA hiç kapatılamaz ve kullanıcı kendi
// cihazında da bir sonraki token yenilemede çıkış yapar.
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/api/api_client.dart';
import 'package:karar/core/auth/auth_service.dart';
import 'package:karar/core/storage/secure_storage.dart';

class _FakeStorage implements SecureStorage {
  _FakeStorage({this.refreshToken});

  final String? refreshToken;

  @override
  Future<String?> readAccessToken() async => 'access-token';

  @override
  Future<String?> readRefreshToken() async => refreshToken;

  @override
  dynamic noSuchMethod(Invocation invocation) => Future<Null>.value();
}

class _RecordingBackend {
  final Map<String, Object?> bodies = {};

  Dio dio() {
    final dio = Dio(BaseOptions(baseUrl: 'http://localhost'));
    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) {
          final key = '${options.method} ${options.path}';
          bodies[key] = options.data;
          final Object? body = key == 'GET /api/v1/users/me'
              ? {
                  'id': 'user-1',
                  'username': 'ali',
                  'email': 'ali@example.com',
                  'karma': 0,
                  'authProvider': 'password',
                  'is2faEnabled': true,
                }
              : null;
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
}

AuthService _service(_RecordingBackend backend, {String? refreshToken}) =>
    AuthService(
      storage: _FakeStorage(refreshToken: refreshToken),
      apiClient: ApiClient(dio: backend.dio()),
    );

void main() {
  group('AuthService.disable2fa', () {
    test('şifre ve TOTP kodunu gönderir', () async {
      final backend = _RecordingBackend();
      await _service(backend)
          .disable2fa(password: 'gizli-sifre', secondFactor: ' 123456 ');

      expect(backend.bodies['POST /api/v1/auth/2fa/disable'], {
        'password': 'gizli-sifre',
        'code': '123456',
      });
    });

    test('6 haneli olmayan girişi yedek kod olarak gönderir', () async {
      final backend = _RecordingBackend();
      await _service(backend)
          .disable2fa(password: 'gizli-sifre', secondFactor: 'ABCD-EFGH');

      expect(backend.bodies['POST /api/v1/auth/2fa/disable'], {
        'password': 'gizli-sifre',
        'backupCode': 'ABCD-EFGH',
      });
    });

    test('şifresiz (Google) hesapta yalnızca ikinci faktörü gönderir',
        () async {
      final backend = _RecordingBackend();
      await _service(backend).disable2fa(secondFactor: '654321');

      expect(backend.bodies['POST /api/v1/auth/2fa/disable'], {
        'code': '654321',
      });
    });
  });

  group('Kimlik bilgisi değişikliği bu oturumu korur', () {
    test('changePassword mevcut refresh token\'ı gönderir', () async {
      final backend = _RecordingBackend();
      await _service(backend, refreshToken: 'rt-1').changePassword(
        currentPassword: 'eski-sifre',
        newPassword: 'yeni-sifre-123',
      );

      expect(backend.bodies['PUT /api/v1/users/me/password'], {
        'currentPassword': 'eski-sifre',
        'newPassword': 'yeni-sifre-123',
        'refreshToken': 'rt-1',
      });
    });

    test('confirmEmailChange mevcut refresh token\'ı gönderir', () async {
      final backend = _RecordingBackend();
      await _service(backend, refreshToken: 'rt-2').confirmEmailChange(
        newEmail: 'yeni@example.com',
        otp: '111111',
      );

      expect(backend.bodies['POST /api/v1/auth/change-email/confirm'], {
        'newEmail': 'yeni@example.com',
        'otp': '111111',
        'refreshToken': 'rt-2',
      });
    });

    test('refresh token yoksa alan hiç gönderilmez', () async {
      final backend = _RecordingBackend();
      await _service(backend).changePassword(
        currentPassword: 'eski-sifre',
        newPassword: 'yeni-sifre-123',
      );

      expect(
        (backend.bodies['PUT /api/v1/users/me/password']! as Map)
            .containsKey('refreshToken'),
        isFalse,
      );
    });
  });
}
