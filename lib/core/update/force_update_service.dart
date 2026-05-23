import '../api/api_client.dart';
import '../app_services.dart';

class VersionInfo {
  const VersionInfo({
    required this.minimumVersion,
    required this.androidStoreUrl,
    required this.iosStoreUrl,
  });

  final String minimumVersion;
  final String androidStoreUrl;
  final String iosStoreUrl;
}

class ForceUpdateService {
  const ForceUpdateService({required ApiClient apiClient})
      : _apiClient = apiClient;

  final ApiClient _apiClient;

  static const _currentVersion = '1.0.0';

  static const _androidStoreUrl =
      'https://play.google.com/store/apps/details?id=app.karar';
  static const _iosStoreUrl =
      'https://apps.apple.com/app/karar/id0000000000';

  Future<VersionInfo?> checkForUpdate() async {
    if (!AppRuntime.useRemoteApi) return null;

    try {
      final json = await _apiClient.getJson<Map<String, Object?>>(
        '/api/v1/version',
      );
      final minVersion = json['minimumVersion'] as String?;
      if (minVersion == null) return null;

      if (!_isUpdateRequired(minVersion)) return null;

      return VersionInfo(
        minimumVersion: minVersion,
        androidStoreUrl: json['androidStoreUrl'] as String? ?? _androidStoreUrl,
        iosStoreUrl: json['iosStoreUrl'] as String? ?? _iosStoreUrl,
      );
    } catch (_) {
      return null;
    }
  }

  bool _isUpdateRequired(String minimumVersion) {
    final current = _parseVersion(_currentVersion);
    final minimum = _parseVersion(minimumVersion);
    if (current == null || minimum == null) return false;
    return _compareVersions(current, minimum) < 0;
  }

  List<int>? _parseVersion(String v) {
    try {
      return v.split('.').map(int.parse).toList();
    } catch (_) {
      return null;
    }
  }

  int _compareVersions(List<int> a, List<int> b) {
    for (var i = 0; i < 3; i++) {
      final ai = i < a.length ? a[i] : 0;
      final bi = i < b.length ? b[i] : 0;
      if (ai != bi) return ai.compareTo(bi);
    }
    return 0;
  }
}
