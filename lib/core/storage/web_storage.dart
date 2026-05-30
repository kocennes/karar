// ignore_for_file: avoid_web_libraries_in_flutter, deprecated_member_use

import 'dart:html' as html;

abstract class WebStorageBase {
  Future<String?> read(String key);
  Future<void> write(String key, String value);
  Future<void> delete(String key);
  Future<void> clearAll();
}

WebStorageBase createWebStorage() => _LocalStorageImpl();

class _LocalStorageImpl implements WebStorageBase {
  final _store = html.window.localStorage;

  @override
  Future<String?> read(String key) async => _store[key];

  @override
  Future<void> write(String key, String value) async => _store[key] = value;

  @override
  Future<void> delete(String key) async => _store.remove(key);

  @override
  Future<void> clearAll() async {
    _store.remove('access_token');
    _store.remove('refresh_token');
    _store.remove('device_token');
    _store.remove('pending_email_change');
  }
}
