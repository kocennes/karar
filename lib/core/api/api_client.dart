import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../config/app_config.dart';
import 'api_exception.dart';

class SseEvent {
  const SseEvent({required this.type, required this.data});
  final String type;
  final String data;
}

typedef TokenReader = Future<String?> Function();
typedef TokenRefresher = Future<String?> Function();

class ApiClient {
  ApiClient({
    String baseUrl = AppConfig.apiBaseUrl,
    TokenReader? deviceTokenReader,
    TokenReader? accessTokenReader,
    TokenRefresher? tokenRefresher,
    Dio? dio,
    void Function()? onMaintenance,
  })  : _deviceTokenReader = deviceTokenReader,
        _accessTokenReader = accessTokenReader,
        _tokenRefresher = tokenRefresher,
        _onMaintenance = onMaintenance,
        _dio = dio ?? _buildDio(baseUrl);

  final Dio _dio;
  final TokenReader? _deviceTokenReader;
  final TokenReader? _accessTokenReader;
  final TokenRefresher? _tokenRefresher;
  void Function()? _onMaintenance;

  void setOnMaintenance(void Function() callback) =>
      _onMaintenance = callback;

  static Dio _buildDio(String baseUrl) => Dio(
        BaseOptions(
          baseUrl: baseUrl,
          connectTimeout: const Duration(seconds: 10),
          receiveTimeout: const Duration(seconds: 15),
          headers: {'Content-Type': 'application/json'},
        ),
      );

  Future<Map<String, String>> _authHeaders() async {
    final headers = <String, String>{};

    final accessToken = await _accessTokenReader?.call();
    if (accessToken != null && accessToken.isNotEmpty) {
      headers['Authorization'] = 'Bearer $accessToken';
    }

    final deviceToken = await _deviceTokenReader?.call();
    if (deviceToken != null && deviceToken.isNotEmpty) {
      headers['X-Device-Token'] = deviceToken;
    }

    return headers;
  }

  Future<Map<String, String>> _headersWithAdmin(String? adminToken) async {
    if (adminToken != null) {
      return {'Authorization': 'Bearer $adminToken'};
    }
    return _authHeaders();
  }

  Future<T> getJson<T>(
    String path, {
    Map<String, String?> queryParams = const {},
    // ignore: avoid_unused_constructor_parameters
    Map<String, String?> query = const {},
    String? adminToken,
  }) =>
      _request<T>(
        () async => _dio.get<dynamic>(
          path,
          queryParameters: {
            for (final e in {...query, ...queryParams}.entries)
              if (e.value != null && e.value!.isNotEmpty) e.key: e.value,
          },
          options: Options(headers: await _headersWithAdmin(adminToken)),
        ),
      );

  Future<Uint8List> getBytes(String path) async {
    final headers = await _authHeaders();
    final response = await _dio.get<List<int>>(
      path,
      options: Options(
        responseType: ResponseType.bytes,
        headers: headers,
      ),
    );
    return Uint8List.fromList(response.data ?? []);
  }

  Future<T> postJson<T>(String path, {Object? body, String? adminToken}) =>
      _request<T>(
        () async => _dio.post<dynamic>(
          path,
          data: body,
          options: Options(headers: await _headersWithAdmin(adminToken)),
        ),
      );

  Future<T> putJson<T>(String path, {Object? body, String? adminToken}) =>
      _request<T>(
        () async => _dio.put<dynamic>(
          path,
          data: body,
          options: Options(headers: await _headersWithAdmin(adminToken)),
        ),
      );

  Future<T> deleteJson<T>(
          String path, {Object? body, String? adminToken}) =>
      _request<T>(
        () async => _dio.delete<dynamic>(
          path,
          data: body,
          options: Options(headers: await _headersWithAdmin(adminToken)),
        ),
      );

  Future<T> postMultipart<T>(String path, FormData formData) => _request<T>(
        () async => _dio.post<dynamic>(
          path,
          data: formData,
          options: Options(
            headers: {
              ...await _authHeaders(),
              'Content-Type': 'multipart/form-data',
            },
          ),
        ),
      );

  Future<T> _request<T>(Future<Response<dynamic>> Function() call) async {
    try {
      final response = await call();
      if ((response.statusCode ?? 0) >= 400) {
        throw _toApiException(_dioExceptionForResponse(response));
      }
      if (response.statusCode == 204) return null as T;
      return response.data as T;
    } on DioException catch (e) {
      if (e.response?.statusCode == 503) {
        _onMaintenance?.call();
      }
      if (e.response?.statusCode == 401 && _tokenRefresher != null) {
        final newToken = await _tokenRefresher();
        if (newToken != null) {
          try {
            final retried = await call();
            if ((retried.statusCode ?? 0) >= 400) {
              throw _toApiException(_dioExceptionForResponse(retried));
            }
            if (retried.statusCode == 204) return null as T;
            return retried.data as T;
          } on DioException catch (retryErr) {
            throw _toApiException(retryErr);
          }
        }
      }
      throw _toApiException(e);
    }
  }

  Stream<SseEvent> sseStream(String path) async* {
    final headers = await _authHeaders();
    late final Response<ResponseBody> response;
    try {
      response = await _dio.get<ResponseBody>(
        path,
        options: Options(
          responseType: ResponseType.stream,
          receiveTimeout: const Duration(hours: 1),
          headers: {
            ...headers,
            'Accept': 'text/event-stream',
            'Cache-Control': 'no-cache',
          },
        ),
      );
    } on DioException {
      return;
    }

    String? eventType;
    final buffer = StringBuffer();

    await for (final chunk in response.data!.stream
        .map<List<int>>((chunk) => chunk)
        .transform(utf8.decoder)
        .transform(const LineSplitter())) {
      if (chunk.startsWith('event: ')) {
        eventType = chunk.substring(7).trim();
      } else if (chunk.startsWith('data: ')) {
        buffer.write(chunk.substring(6));
      } else if (chunk.isEmpty && buffer.isNotEmpty) {
        yield SseEvent(type: eventType ?? 'message', data: buffer.toString());
        eventType = null;
        buffer.clear();
      }
    }
  }

  ApiException _toApiException(DioException e) {
    if (e.response == null) {
      return const ApiException(
        statusCode: 0,
        code: 'NETWORK_ERROR',
        message: 'İnternet bağlantınızı kontrol edin.',
      );
    }

    final data = e.response?.data;
    final retryAfter = e.response?.headers.value('retry-after');
    final retrySeconds = retryAfter != null ? int.tryParse(retryAfter) : null;

    if (data is Map<String, dynamic>) {
      final error = data['error'] as Map<String, dynamic>?;
      if (error != null) {
        return ApiException(
          statusCode: e.response!.statusCode!,
          code: error['code'] as String? ?? 'UNKNOWN',
          message: error['message'] as String? ?? 'Bilinmeyen hata.',
          retryAfterSeconds:
              (error['retryAfterSeconds'] as int?) ?? retrySeconds,
        );
      }
    }
    return ApiException(
      statusCode: e.response?.statusCode ?? 0,
      code: 'HTTP_${e.response?.statusCode ?? 0}',
      message: 'Beklenmeyen API yanıtı.',
      retryAfterSeconds: retrySeconds,
    );
  }

  DioException _dioExceptionForResponse(Response<dynamic> response) {
    return DioException(
      requestOptions: response.requestOptions,
      response: response,
      type: DioExceptionType.badResponse,
    );
  }
}
