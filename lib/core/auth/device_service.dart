import 'package:flutter/foundation.dart';

import '../api/api_client.dart';
import 'device_token_store.dart';

class DeviceService {
  DeviceService({required this.apiClient, required this.tokenStore});

  final ApiClient apiClient;
  final DeviceTokenStore tokenStore;

  Future<String> getOrRegisterDeviceToken() async {
    final stored = await tokenStore.read();
    if (stored != null && stored.isNotEmpty) {
      return stored;
    }

    final session = await apiClient.postJson<Map<String, Object?>>(
      '/api/v1/devices/register',
      body: {
        'fingerprint': _fingerprint(),
        'platform': _platform(),
        'appVersion': '1.0.0',
      },
    );
    final token = session['deviceToken'] as String;
    await tokenStore.write(token);
    return token;
  }

  Future<void> registerFcmToken(String token) async {
    await apiClient.putJson<void>(
      '/api/v1/devices/fcm-token',
      body: {'token': token, 'platform': _platform()},
    );
  }

  String _fingerprint() => 'karar-${_platform()}-dev';

  String _platform() {
    if (kIsWeb) {
      return 'web';
    }

    return switch (defaultTargetPlatform) {
      TargetPlatform.android => 'android',
      TargetPlatform.iOS => 'ios',
      _ => 'web',
    };
  }
}
