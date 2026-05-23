import 'package:firebase_remote_config/firebase_remote_config.dart';
import 'package:flutter/foundation.dart';

/// Firebase Remote Config wrapper.
///
/// Default values are defined here so the app works correctly before the first
/// successful fetch or when Remote Config is unavailable.
///
/// Usage:
///   final config = ref.read(remoteConfigServiceProvider);
///   if (config.isFeatureEnabled(RemoteConfigKeys.adsEnabled)) { ... }
class RemoteConfigService {
  RemoteConfigService._();

  static final RemoteConfigService _instance = RemoteConfigService._();
  factory RemoteConfigService() => _instance;

  FirebaseRemoteConfig? _rc;

  static const Map<String, dynamic> _defaults = {
    RemoteConfigKeys.adsEnabled: true,
    RemoteConfigKeys.dailyPostLimit: 5,
    RemoteConfigKeys.voteThresholdForVerdict: 100,
    RemoteConfigKeys.minVotesForPercentage: 40,
    RemoteConfigKeys.reportThresholdAuto: 10,
    RemoteConfigKeys.maintenanceMode: false,
    RemoteConfigKeys.maintenanceMessage: '',
    RemoteConfigKeys.forceUpdateMinVersion: '',
    RemoteConfigKeys.wellbeingSessionLimit: 20,
    RemoteConfigKeys.newUserPostLimit: 2,
    RemoteConfigKeys.communityHealthBannerEnabled: true,
  };

  Future<void> init() async {
    try {
      _rc = FirebaseRemoteConfig.instance;
      await _rc!.setConfigSettings(RemoteConfigSettings(
        fetchTimeout: const Duration(seconds: 10),
        minimumFetchInterval: kDebugMode
            ? const Duration(minutes: 1)
            : const Duration(hours: 1),
      ));
      await _rc!.setDefaults(_defaults);
      await _rc!.fetchAndActivate();
    } catch (e) {
      debugPrint('RemoteConfig init failed: $e');
      _rc = null;
    }
  }

  bool getBool(String key) {
    if (_rc == null) return _defaults[key] as bool? ?? false;
    return _rc!.getBool(key);
  }

  int getInt(String key) {
    if (_rc == null) return _defaults[key] as int? ?? 0;
    return _rc!.getInt(key);
  }

  String getString(String key) {
    if (_rc == null) return _defaults[key] as String? ?? '';
    return _rc!.getString(key);
  }

  bool isFeatureEnabled(String key) => getBool(key);
}

abstract final class RemoteConfigKeys {
  static const String adsEnabled = 'ads_enabled';
  static const String dailyPostLimit = 'daily_post_limit';
  static const String voteThresholdForVerdict = 'vote_threshold_for_verdict';
  static const String minVotesForPercentage = 'min_votes_for_percentage';
  static const String reportThresholdAuto = 'report_threshold_auto';
  static const String maintenanceMode = 'maintenance_mode';
  static const String maintenanceMessage = 'maintenance_message';
  static const String forceUpdateMinVersion = 'force_update_min_version';
  static const String wellbeingSessionLimit = 'wellbeing_session_limit';
  static const String newUserPostLimit = 'new_user_post_limit';
  static const String communityHealthBannerEnabled =
      'community_health_banner_enabled';
}
