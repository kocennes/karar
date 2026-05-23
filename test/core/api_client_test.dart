import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/api/api_client.dart';
import 'package:karar/core/api/api_exception.dart';

Dio _mockDio({required int statusCode, required Map<String, dynamic> body}) {
  final dio = Dio(BaseOptions(baseUrl: 'http://localhost'));
  dio.interceptors.add(
    InterceptorsWrapper(
      onRequest: (options, handler) => handler.resolve(
        Response(
          requestOptions: options,
          statusCode: statusCode,
          data: body,
        ),
      ),
    ),
  );
  return dio;
}

void main() {
  test('ApiClient sends device token header when no access token', () async {
    String? capturedHeader;

    final dio = Dio(BaseOptions(baseUrl: 'http://localhost'));
    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) {
          capturedHeader = options.headers['X-Device-Token'] as String?;
          handler.resolve(
            Response(
              requestOptions: options,
              statusCode: 200,
              data: {'ok': true},
            ),
          );
        },
      ),
    );

    final client = ApiClient(
      deviceTokenReader: () async => 'dt_test',
      dio: dio,
    );

    await client.getJson<Map<String, Object?>>('/health');

    expect(capturedHeader, 'dt_test');
  });

  test('ApiClient keeps device token header when access token exists',
      () async {
    String? capturedDeviceHeader;
    String? capturedAuthHeader;

    final dio = Dio(BaseOptions(baseUrl: 'http://localhost'));
    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) {
          capturedDeviceHeader = options.headers['X-Device-Token'] as String?;
          capturedAuthHeader = options.headers['Authorization'] as String?;
          handler.resolve(
            Response(
              requestOptions: options,
              statusCode: 200,
              data: {'ok': true},
            ),
          );
        },
      ),
    );

    final client = ApiClient(
      deviceTokenReader: () async => 'dt_test',
      accessTokenReader: () async => 'jwt_test',
      dio: dio,
    );

    await client.getJson<Map<String, Object?>>('/health');

    expect(capturedDeviceHeader, 'dt_test');
    expect(capturedAuthHeader, 'Bearer jwt_test');
  });

  test('ApiClient maps documented error envelope to ApiException', () {
    final client = ApiClient(
      dio: _mockDio(
        statusCode: 400,
        body: {
          'error': {'code': 'VALIDATION_ERROR', 'message': 'Hatalı istek.'},
        },
      ),
    );

    expect(
      () => client.postJson<Map<String, Object?>>('/api/v1/posts'),
      throwsA(
        isA<ApiException>()
            .having((e) => e.statusCode, 'statusCode', 400)
            .having((e) => e.code, 'code', 'VALIDATION_ERROR')
            .having((e) => e.message, 'message', 'Hatalı istek.'),
      ),
    );
  });

  test('ApiClient treats 204 delete response as void success', () async {
    final client = ApiClient(
      dio: _mockDio(statusCode: 204, body: {}),
    );

    await expectLater(client.deleteJson<void>('/api/v1/users/me'), completes);
  });

  test('ApiClient sends JSON body with delete request', () async {
    Object? capturedBody;

    final dio = Dio(BaseOptions(baseUrl: 'http://localhost'));
    dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) {
          capturedBody = options.data;
          handler.resolve(
            Response(
              requestOptions: options,
              statusCode: 204,
              data: null,
            ),
          );
        },
      ),
    );

    final client = ApiClient(dio: dio);

    await client.deleteJson<void>(
      '/api/v1/users/me',
      body: {'password': 'strong-password'},
    );

    expect(capturedBody, {'password': 'strong-password'});
  });
}
