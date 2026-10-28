import 'package:flutter/material.dart' show DateUtils;

/// P0-02: temporary neutral age gate, in place until the e-Devlet age token.
///
/// "Neutral": the UI never shows the threshold, the date picker does not lean
/// toward an adult year, and a refusal neither explains why nor suggests that
/// another date would work. Once a device is refused it stays refused (a lock
/// in secure storage here, `devices.age_gate_failed_at` on the server).
///
/// API contract (backend `feat/age-gate-api`):
/// * `requiresOnboarding` (bool, camelCase) on `/auth/google`, `/auth/login`,
///   `/auth/verify-email` and `GET /users/me`.
/// * `POST /api/v1/users/me/onboarding`
///   `{dateOfBirth, acceptedTerms, acceptedCommunityGuidelines}` →
///   `200 {"requiresOnboarding": false}`.
/// * `POST /api/v1/devices/age-declaration` `{dateOfBirth}` → `204`.
/// * Error codes below, in the usual `{error: {code, message}}` envelope.
abstract final class AgeGate {
  // Not shown anywhere in the UI.
  static const int _minimumAge = 18;

  /// 403: the declared date is not eligible, or this device was refused before.
  static const notEligibleCode = 'AGE_NOT_ELIGIBLE';

  /// 403 from a write endpoint (server flag `AgeGate:Enforce`): the account has
  /// no birth date yet.
  static const verificationRequiredCode = 'AGE_VERIFICATION_REQUIRED';

  /// 403 from a guest vote (server flag `AgeGate:GuestVote`).
  static const declarationRequiredCode = 'AGE_DECLARATION_REQUIRED';

  /// 400: a future or implausible date (a typo, not an age statement).
  static const invalidDateCode = 'INVALID_DATE_OF_BIRTH';

  static const refusalTitle = 'Kayıt tamamlanamadı';
  static const refusalMessage = "Üzgünüz, Karar'ı kullanamıyorsun.";

  static DateTime today([DateTime? now]) =>
      DateUtils.dateOnly(now ?? DateTime.now());

  static int ageOn(DateTime dateOfBirth, DateTime today) {
    var age = today.year - dateOfBirth.year;
    final hadBirthday = today.month > dateOfBirth.month ||
        (today.month == dateOfBirth.month && today.day >= dateOfBirth.day);
    if (!hadBirthday) age--;
    return age;
  }

  static bool isEligible(DateTime dateOfBirth, {DateTime? now}) =>
      ageOn(dateOfBirth, today(now)) >= _minimumAge;
}

/// Routes a signed-in account without a birth date to the onboarding step.
/// Used as the GoRouter `redirect`; returns null when no redirect is needed.
String? ageOnboardingRedirect({
  required bool requiresOnboarding,
  required String location,
}) {
  if (!requiresOnboarding) return null;
  const open = {
    ageOnboardingPath,
    '/splash',
    '/onboarding',
    '/policy-acceptance',
  };
  if (open.contains(location) || location.startsWith('/legal/')) return null;
  return ageOnboardingPath;
}

const ageOnboardingPath = '/auth/onboarding';
