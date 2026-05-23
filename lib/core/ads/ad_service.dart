import 'package:flutter/foundation.dart';
import 'package:google_mobile_ads/google_mobile_ads.dart';

class AdService {
  AdService._();
  static final instance = AdService._();

  bool _isMobileAdsStartCalled = false;

  Future<void> init() async {
    if (kIsWeb) return;

    final params = ConsentRequestParameters();

    ConsentInformation.instance.requestConsentInfoUpdate(
      params,
      () async {
        if (await ConsentInformation.instance.isConsentFormAvailable()) {
          _loadAndShowConsentFormIfRequired();
        } else {
          _initializeMobileAds();
        }
      },
      (error) {
        debugPrint('Consent error: ${error.message}');
        _initializeMobileAds();
      },
    );
  }

  void _loadAndShowConsentFormIfRequired() {
    ConsentForm.loadConsentForm(
      (consentForm) {
        ConsentInformation.instance.getConsentStatus().then((status) {
          if (status == ConsentStatus.required) {
            consentForm.show((formError) {
              _loadAndShowConsentFormIfRequired();
            });
          } else {
            _initializeMobileAds();
          }
        });
      },
      (formError) {
        debugPrint('Consent form error: ${formError.message}');
        _initializeMobileAds();
      },
    );
  }

  void _initializeMobileAds() {
    if (_isMobileAdsStartCalled) return;
    _isMobileAdsStartCalled = true;
    MobileAds.instance.initialize();
  }

  Future<void> showPrivacyOptionsForm() async {
    if (kIsWeb) return;

    ConsentForm.showPrivacyOptionsForm((error) {
      if (error != null) {
        debugPrint('Privacy options error: ${error.message}');
      }
    });
  }
}
