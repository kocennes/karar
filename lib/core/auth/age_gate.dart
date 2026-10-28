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
/// * Error codes below, in the usual `{error: {code, message}}` envelope. The
///   app answers them in one place: `ApiClient` → `AgeGateCoordinator`.
abstract final class AgeGate {
  // Not shown anywhere in the UI.
  static const int _minimumAge = 18;

  /// 403: the declared date is not eligible, the device or the account was
  /// refused before. Can come from register, onboarding, `/auth/google`,
  /// `/auth/login`, the device declaration and (with `AgeGate:Enforce`) any
  /// write.
  static const notEligibleCode = 'AGE_NOT_ELIGIBLE';

  /// 403 from a write endpoint (server flag `AgeGate:Enforce`): the account has
  /// no birth date yet.
  static const verificationRequiredCode = 'AGE_VERIFICATION_REQUIRED';

  /// 403 from a guest vote (server flag `AgeGate:GuestVote`).
  static const declarationRequiredCode = 'AGE_DECLARATION_REQUIRED';

  /// 400: a future or implausible date (a typo, not an age statement).
  static const invalidDateCode = 'INVALID_DATE_OF_BIRTH';

  /// Fits a refused sign-up, a refused sign-in and a refused existing account.
  static const refusalTitle = 'Devam edilemiyor';
  static const refusalMessage = "Üzgünüz, Karar'ı kullanamıyorsun.";

  /// Asks for the birth date (onboarding step, guest declaration sheet).
  static const birthDatePrompt = 'Devam etmek için doğum tarihini gir.';

  /// The server's offset: Turkey keeps UTC+3 all year (no DST).
  static const _serverOffset = Duration(hours: 3);

  /// Today's date in Turkey, the calendar the server checks ages against,
  /// whatever the device's time zone. Date part only. Use this everywhere a
  /// birth date is compared with "today" (eligibility, the picker's end).
  static DateTime today([DateTime? now]) {
    final turkey = (now ?? DateTime.now()).toUtc().add(_serverOffset);
    return DateTime(turkey.year, turkey.month, turkey.day);
  }

  static int ageOn(DateTime dateOfBirth, DateTime today) {
    var age = today.year - dateOfBirth.year;
    final hadBirthday = today.month > dateOfBirth.month ||
        (today.month == dateOfBirth.month && today.day >= dateOfBirth.day);
    if (!hadBirthday) age--;
    return age;
  }

  /// Local pre-check only (register screen). The server has the final word.
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
    ageRefusalPath,
    '/splash',
    '/onboarding',
    '/policy-acceptance',
  };
  if (open.contains(location) || location.startsWith('/legal/')) return null;
  return ageOnboardingPath;
}

const ageOnboardingPath = '/auth/onboarding';

/// The neutral refusal page, for refusals outside the auth screens (which
/// show it in place).
const ageRefusalPath = '/age-gate';
