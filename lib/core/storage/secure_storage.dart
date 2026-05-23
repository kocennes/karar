import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'storage_impl.dart';

class SecureStorage {
  SecureStorage()
      : _nativeStorage = kIsWeb ? null : const FlutterSecureStorage(),
        _webStorage = kIsWeb ? createWebStorage() : null;

  final FlutterSecureStorage? _nativeStorage;
  final WebStorageBase? _webStorage;

  static const _keyAccessToken = 'access_token';
  static const _keyRefreshToken = 'refresh_token';
  static const _keyDeviceToken = 'device_token';
  static const _keyPendingEmailChange = 'pending_email_change';

  Future<String?> _read(String key) async {
    if (kIsWeb) return _webStorage!.read(key);
    return _nativeStorage!.read(key: key);
  }

  Future<void> _write(String key, String value) async {
    if (kIsWeb) return _webStorage!.write(key, value);
    return _nativeStorage!.write(key: key, value: value);
  }

  Future<void> _delete(String key) async {
    if (kIsWeb) return _webStorage!.delete(key);
    return _nativeStorage!.delete(key: key);
  }

  Future<String?> readAccessToken() => _read(_keyAccessToken);
  Future<void> writeAccessToken(String token) =>
      _write(_keyAccessToken, token);

  Future<String?> readRefreshToken() => _read(_keyRefreshToken);
  Future<void> writeRefreshToken(String token) =>
      _write(_keyRefreshToken, token);

  Future<String?> readDeviceToken() => _read(_keyDeviceToken);
  Future<void> writeDeviceToken(String token) =>
      _write(_keyDeviceToken, token);

  Future<void> clearAuthTokens() async {
    await _delete(_keyAccessToken);
    await _delete(_keyRefreshToken);
  }

  Future<String?> readPendingEmailChange() => _read(_keyPendingEmailChange);
  Future<void> writePendingEmailChange(String email) =>
      _write(_keyPendingEmailChange, email);
  Future<void> clearPendingEmailChange() => _delete(_keyPendingEmailChange);

  Future<void> clearAll() async {
    if (kIsWeb) {
      await _webStorage!.clearAll();
    } else {
      await _nativeStorage!.deleteAll();
    }
  }
}
