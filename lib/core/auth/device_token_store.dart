import '../storage/secure_storage.dart';

abstract interface class DeviceTokenStore {
  Future<String?> read();

  Future<void> write(String token);
}

class SecureDeviceTokenStore implements DeviceTokenStore {
  const SecureDeviceTokenStore(this._storage);

  final SecureStorage _storage;

  @override
  Future<String?> read() => _storage.readDeviceToken();

  @override
  Future<void> write(String token) => _storage.writeDeviceToken(token);
}
