import 'package:firebase_auth/firebase_auth.dart';
import 'package:google_sign_in/google_sign_in.dart';

import '../api/api_client.dart';
import '../api/api_endpoints.dart';
import '../api/api_exception.dart';
import '../storage/secure_storage.dart';
import '../web/tab_auth_sync.dart';

class AuthUser {
  const AuthUser({
    required this.id,
    required this.username,
    required this.email,
    required this.karma,
    required this.authProvider,
    this.bio,
    this.usernameChangedAt,
    this.postCount = 0,
    this.commentCount = 0,
    this.joinedAt,
    this.isNewUser = false,
    this.is2faEnabled = false,
  });

  final String id;
  final String username;
  final String email;
  final int karma;
  final String authProvider;
  final String? bio;
  final DateTime? usernameChangedAt;
  final int postCount;
  final int commentCount;
  final DateTime? joinedAt;
  final bool isNewUser;
  final bool is2faEnabled;

  bool get canChangeUsername {
    if (usernameChangedAt == null) return true;
    return DateTime.now().difference(usernameChangedAt!).inDays >= 30;
  }

  factory AuthUser.fromJson(Map<String, Object?> json) {
    final user = json['user'] as Map<String, Object?>? ?? json;
    return AuthUser(
      id: (user['id'] ?? user['userId']) as String,
      username: user['username'] as String,
      email: user['email'] as String,
      karma: user['karma'] as int? ?? 0,
      authProvider: user['authProvider'] as String? ?? 'password',
      bio: user['bio'] as String?,
      usernameChangedAt:
          DateTime.tryParse(user['usernameChangedAt'] as String? ?? ''),
      postCount: user['postCount'] as int? ?? 0,
      commentCount: user['commentCount'] as int? ?? 0,
      joinedAt: DateTime.tryParse(user['joinedAt'] as String? ?? ''),
      isNewUser: json['isNewUser'] as bool? ?? false,
      is2faEnabled: user['is2faEnabled'] as bool? ?? false,
    );
  }
}

enum LogoutReason { manual, sessionExpired }

class AuthService {
  AuthService({
    required SecureStorage storage,
    required ApiClient apiClient,
    void Function(LogoutReason)? onLogout,
    Future<void> Function()? onBeforeLogout,
  })  : _storage = storage,
        _apiClient = apiClient,
        _onBeforeLogout = onBeforeLogout,
        _onLogout = onLogout,
        _googleSignIn = GoogleSignIn(scopes: ['email']);

  final SecureStorage _storage;
  final ApiClient _apiClient;
  final GoogleSignIn _googleSignIn;
  Future<void> Function()? _onBeforeLogout;
  void Function(LogoutReason)? _onLogout;

  void setOnLogout(void Function(LogoutReason) callback) =>
      _onLogout = callback;

  void setOnBeforeLogout(Future<void> Function() callback) =>
      _onBeforeLogout = callback;

  AuthUser? _currentUser;
  AuthUser? get currentUser => _currentUser;
  bool get isLoggedIn => _currentUser != null;

  String? _pendingEmailChange;
  String? get pendingEmailChange => _pendingEmailChange;

  Future<String?> readAccessToken() => _storage.readAccessToken();

  Future<void> init() async {
    _pendingEmailChange = await _storage.readPendingEmailChange();

    final accessToken = await _storage.readAccessToken();
    if (accessToken == null) return;

    try {
      final json = await _apiClient.getJson<Map<String, Object?>>(
        ApiEndpoints.userMe,
      );
      _currentUser = AuthUser.fromJson(json);
    } catch (_) {
      // Token geçersiz ya da süresi dolmuş; refresh dene
      final refreshed = await refreshAccessToken();
      if (refreshed == null) {
        await _storage.clearAuthTokens();
      }
    }
  }

  Future<void> setPendingEmailChange(String email) async {
    _pendingEmailChange = email;
    await _storage.writePendingEmailChange(email);
  }

  Future<void> clearPendingEmailChange() async {
    _pendingEmailChange = null;
    await _storage.clearPendingEmailChange();
  }

  Future<String> register({
    required String username,
    required String email,
    required String password,
    required DateTime dateOfBirth,
    required String gender,
    required bool acceptedTerms,
    required bool acceptedCommunityGuidelines,
    required bool ageConfirmed,
  }) async {
    await _apiClient.postJson<Map<String, Object?>>(
      ApiEndpoints.authRegister,
      body: {
        'username': username,
        'email': email,
        'password': password,
        'dateOfBirth': dateOfBirth.toIso8601String(),
        'gender': gender,
        'acceptedTerms': acceptedTerms,
        'acceptedCommunityGuidelines': acceptedCommunityGuidelines,
        'ageConfirmed': ageConfirmed,
      },
    );
    return email;
  }

  Future<AuthUser> verifyEmail({
    required String email,
    required String otp,
  }) async {
    final json = await _apiClient.postJson<Map<String, Object?>>(
      ApiEndpoints.authVerifyEmail,
      body: {'email': email, 'otp': otp},
    );
    return _saveSession(json);
  }

  Future<void> resendOtp(String email) => _apiClient.postJson<void>(
        ApiEndpoints.authResendOtp,
        body: {'email': email},
      );

  Future<AuthUser> loginWithGoogle() async {
    final googleUser = await _googleSignIn.signIn();
    if (googleUser == null) throw Exception('Google girişi iptal edildi.');

    final googleAuth = await googleUser.authentication;
    final credential = GoogleAuthProvider.credential(
      accessToken: googleAuth.accessToken,
      idToken: googleAuth.idToken,
    );

    final userCredential =
        await FirebaseAuth.instance.signInWithCredential(credential);
    final idToken = await userCredential.user?.getIdToken();
    if (idToken == null) throw Exception('Firebase ID token alınamadı.');

    final json = await _apiClient.postJson<Map<String, Object?>>(
      ApiEndpoints.authGoogle,
      body: {'idToken': idToken},
    );
    return _saveSession(json);
  }

  Future<AuthUser> loginWithPassword({
    required String identifier,
    required String password,
    String? totpCode,
    String? backupCode,
  }) async {
    final json = await _apiClient.postJson<Map<String, Object?>>(
      ApiEndpoints.authLogin,
      body: {
        'identifier': identifier,
        'password': password,
        if (totpCode != null) 'totpCode': totpCode,
        if (backupCode != null) 'backupCode': backupCode,
      },
    );
    return _saveSession(json);
  }

  Future<void> logout({LogoutReason reason = LogoutReason.manual}) async {
    await _onBeforeLogout?.call();
    final refreshToken = await _storage.readRefreshToken();
    if (refreshToken != null && reason == LogoutReason.manual) {
      try {
        await _apiClient.postJson<void>(
          ApiEndpoints.authLogout,
          body: {'refreshToken': refreshToken},
        );
      } catch (_) {}
    }
    await _googleSignIn.signOut();
    await FirebaseAuth.instance.signOut();
    await _storage.clearAuthTokens();
    _currentUser = null;
    TabAuthSync.notifyLoggedOut(); // W36: diğer sekmelere sinyal gönder
    _onLogout?.call(reason);
  }

  Future<void> deleteAccount({String? password}) async {
    await _apiClient.deleteJson<void>(
      ApiEndpoints.userMe,
      body: password == null ? null : {'password': password},
    );
    await _onBeforeLogout?.call();
    await _googleSignIn.signOut();
    await FirebaseAuth.instance.signOut();
    await _storage.clearAuthTokens();
    _currentUser = null;
    TabAuthSync.notifyLoggedOut();
    _onLogout?.call(LogoutReason.manual);
  }

  Future<String?> refreshAccessToken() async {
    final refreshToken = await _storage.readRefreshToken();
    if (refreshToken == null) return null;

    try {
      final json = await _apiClient.postJson<Map<String, Object?>>(
        ApiEndpoints.authRefresh,
        body: {'refreshToken': refreshToken},
      );
      final accessToken = json['accessToken'] as String;
      final newRefreshToken = json['refreshToken'] as String?;
      await _storage.writeAccessToken(accessToken);
      if (newRefreshToken != null) {
        await _storage.writeRefreshToken(newRefreshToken);
      }
      return accessToken;
    } catch (_) {
      await logout(reason: LogoutReason.sessionExpired);
      return null;
    }
  }

  Future<AuthUser> _saveSession(Map<String, Object?> json) async {
    final accessToken = json['accessToken'] as String;
    final refreshToken = json['refreshToken'] as String;
    await _storage.writeAccessToken(accessToken);
    await _storage.writeRefreshToken(refreshToken);
    _currentUser = AuthUser.fromJson(json);
    TabAuthSync.notifyLoggedIn(); // W36: diğer sekmelere sinyal gönder
    return _currentUser!;
  }

  Future<void> changeUsername(String newUsername) async {
    final json = await _apiClient.putJson<Map<String, Object?>>(
      ApiEndpoints.userMe,
      body: {'username': newUsername},
    );
    _currentUser = AuthUser.fromJson(json);
  }

  Future<void> updateProfile({String? username, String? bio}) async {
    final body = <String, Object?>{};
    if (username != null) body['username'] = username;
    if (bio != null) body['bio'] = bio;
    final json = await _apiClient.putJson<Map<String, Object?>>(
      ApiEndpoints.userMe,
      body: body,
    );
    _currentUser = AuthUser.fromJson(json);
  }

  Future<bool> isUsernameAvailable(String username) async {
    try {
      await _apiClient.getJson<void>(
        ApiEndpoints.authCheckUsername,
        queryParams: {'username': username},
      );
      return true;
    } on ApiException catch (e) {
      if (e.statusCode == 409) return false;
      rethrow;
    } catch (_) {
      rethrow;
    }
  }

  Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  }) =>
      _apiClient.putJson<void>(
        ApiEndpoints.userMePassword,
        body: {'currentPassword': currentPassword, 'newPassword': newPassword},
      );

  Future<void> forgotPassword(String email) => _apiClient.postJson<void>(
        ApiEndpoints.authForgotPassword,
        body: {'email': email},
      );

  Future<void> resetPassword({
    required String email,
    required String otp,
    required String newPassword,
  }) =>
      _apiClient.postJson<void>(
        ApiEndpoints.authResetPassword,
        body: {'email': email, 'otp': otp, 'newPassword': newPassword},
      );

  Future<void> submitFeedback({
    required String type,
    required String subject,
    required String message,
    String? contactEmail,
    String appVersion = '1.0.0+45',
    String platform = 'flutter',
  }) =>
      _apiClient.postJson<void>(
        ApiEndpoints.feedback,
        body: {
          'type': type,
          'subject': subject,
          'message': message,
          if (contactEmail != null && contactEmail.trim().isNotEmpty)
            'contactEmail': contactEmail.trim(),
          'appVersion': appVersion,
          'platform': platform,
        },
      );

  Future<Map<String, dynamic>> getNotificationPreferences() async {
    return _apiClient.getJson<Map<String, dynamic>>(
      '${ApiEndpoints.userMe}/notification-preferences',
    );
  }

  Future<void> updatePreferences(Map<String, dynamic> prefs) async {
    await _apiClient.putJson<void>(
      '${ApiEndpoints.userMe}/notification-preferences',
      body: prefs,
    );
  }

  Future<void> requestDataExport() async {
    await _apiClient.getJson<void>(
      '${ApiEndpoints.userMe}/data-export',
    );
  }

  Future<void> blockUser(String userId) async {
    await _apiClient.postJson<void>(
      '${ApiEndpoints.userMe}/blocked',
      body: {'userId': userId},
    );
  }

  Future<void> unblockUser(String userId) async {
    await _apiClient.deleteJson<void>(
      '${ApiEndpoints.userMe}/blocked/$userId',
    );
  }

  Future<List<BlockedUser>> fetchBlockedUsers() async {
    final json = await _apiClient.getJson<List<Object?>>(
      '${ApiEndpoints.userMe}/blocked',
    );
    return json.cast<Map<String, Object?>>().map(BlockedUser.fromJson).toList();
  }

  Future<AuthUser> fetchUserProfile(String username) async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      '/api/v1/users/$username/profile',
    );
    return AuthUser.fromJson(json);
  }

  Future<Map<String, Object?>> setup2fa() async {
    return await _apiClient.postJson<Map<String, Object?>>(
      '/api/v1/auth/2fa/setup',
    );
  }

  Future<void> enable2fa(String code) async {
    await _apiClient.postJson<void>(
      '/api/v1/auth/2fa/enable',
      body: {'code': code},
    );
    await init();
  }

  Future<void> disable2fa() async {
    await _apiClient.postJson<void>(
      '/api/v1/auth/2fa/disable',
    );
    await init();
  }

  Future<List<UserSession>> fetchSessions() async {
    final json = await _apiClient.getJson<List<Object?>>(
      '/api/v1/users/me/sessions',
    );
    return json.cast<Map<String, Object?>>().map(UserSession.fromJson).toList();
  }

  Future<void> revokeSession(String sessionId) async {
    await _apiClient.deleteJson<void>(
      '/api/v1/users/me/sessions/$sessionId',
    );
  }

  Future<List<String>> generateBackupCodes() async {
    final json = await _apiClient.postJson<Map<String, Object?>>(
      ApiEndpoints.auth2faBackupCodes,
    );
    return (json['codes'] as List<Object?>).cast<String>();
  }

  Future<int> getBackupCodeCount() async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.auth2faBackupCodesCount,
    );
    return json['count'] as int? ?? 0;
  }

  Future<void> changeEmail({
    required String newEmail,
    required String password,
  }) =>
      _apiClient.postJson<void>(
        ApiEndpoints.authChangeEmailRequest,
        body: {'newEmail': newEmail, 'password': password},
      );

  Future<void> confirmEmailChange({
    required String newEmail,
    required String otp,
  }) async {
    await _apiClient.postJson<void>(
      ApiEndpoints.authChangeEmailConfirm,
      body: {'newEmail': newEmail, 'otp': otp},
    );
    await init();
  }

  Future<void> recoverAccount(String token) => _apiClient.postJson<void>(
        ApiEndpoints.authRecoverAccount,
        body: {'token': token},
      );

  Future<void> migrateGuestData() => _apiClient.postJson<void>(
        ApiEndpoints.authMigrateGuestData,
      );

  Future<PolicyStatus> checkPolicyStatus() async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.policyStatus,
    );
    return PolicyStatus.fromJson(json);
  }

  Future<void> acceptPolicy({
    required int termsVersion,
    required int privacyVersion,
  }) =>
      _apiClient.postJson<void>(
        ApiEndpoints.acceptPolicy,
        body: {'termsVersion': termsVersion, 'privacyVersion': privacyVersion},
      );

  Future<ModerationSummary> fetchModerationHistory() async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.userMeModerationHistory,
    );
    return ModerationSummary.fromJson(json);
  }

  Future<ReportHistoryPage> fetchReportHistory() async {
    final json = await _apiClient.getJson<Map<String, Object?>>(
      ApiEndpoints.userMeReports,
    );
    return ReportHistoryPage.fromJson(json);
  }

  Future<void> submitModerationAppeal({
    required String targetType,
    required String targetId,
    required String message,
  }) =>
      _apiClient.postJson<void>(
        ApiEndpoints.userMeModerationAppeals,
        body: {
          'targetType': targetType,
          'targetId': targetId,
          'message': message,
        },
      );
}

class PolicyStatus {
  const PolicyStatus({
    required this.needsAcceptance,
    required this.currentTermsVersion,
    required this.currentPrivacyVersion,
  });

  final bool needsAcceptance;
  final int currentTermsVersion;
  final int currentPrivacyVersion;

  factory PolicyStatus.fromJson(Map<String, Object?> json) => PolicyStatus(
        needsAcceptance: json['needsAcceptance'] as bool? ?? false,
        currentTermsVersion: json['currentTermsVersion'] as int? ?? 1,
        currentPrivacyVersion: json['currentPrivacyVersion'] as int? ?? 1,
      );
}

class BlockedUser {
  const BlockedUser({
    required this.id,
    required this.username,
    required this.blockedAt,
  });

  final String id;
  final String username;
  final DateTime blockedAt;

  factory BlockedUser.fromJson(Map<String, Object?> json) {
    return BlockedUser(
      id: json['id'] as String,
      username: json['username'] as String? ?? 'silinmiş_kullanıcı',
      blockedAt: DateTime.parse(json['blockedAt'] as String),
    );
  }
}

class UserSession {
  const UserSession({
    required this.id,
    required this.platform,
    required this.lastSeenAt,
    required this.isCurrent,
  });

  final String id;
  final String platform;
  final DateTime lastSeenAt;
  final bool isCurrent;

  factory UserSession.fromJson(Map<String, Object?> json) {
    return UserSession(
      id: json['id'] as String,
      platform: json['platform'] as String? ?? 'Bilinmeyen',
      lastSeenAt: DateTime.parse(json['lastSeenAt'] as String),
      isCurrent: json['isCurrent'] as bool? ?? false,
    );
  }
}

class ModerationSummary {
  const ModerationSummary({
    required this.activePosts,
    required this.removedPosts,
    required this.warnings,
    required this.events,
  });

  final int activePosts;
  final int removedPosts;
  final int warnings;
  final List<ModerationEvent> events;

  factory ModerationSummary.fromJson(Map<String, Object?> json) {
    final rawEvents = json['events'] as List<Object?>? ?? [];
    return ModerationSummary(
      activePosts: json['activePosts'] as int? ?? 0,
      removedPosts: json['removedPosts'] as int? ?? 0,
      warnings: json['warnings'] as int? ?? 0,
      events: rawEvents
          .cast<Map<String, Object?>>()
          .map(ModerationEvent.fromJson)
          .toList(),
    );
  }
}

class ModerationEvent {
  const ModerationEvent({
    required this.id,
    required this.targetType,
    required this.targetId,
    required this.action,
    required this.reason,
    required this.createdAt,
    required this.appealStatus,
    this.contentExcerpt,
  });

  final String id;
  final String targetType;
  final String targetId;
  final String action; // 'removed', 'warning', 'strike', 'ban'
  final String reason;
  final String? contentExcerpt;
  final DateTime createdAt;
  final String appealStatus; // 'none', 'pending', 'approved', 'rejected'

  factory ModerationEvent.fromJson(Map<String, Object?> json) {
    return ModerationEvent(
      id: json['id'] as String,
      targetType: json['targetType'] as String? ?? 'post',
      targetId: json['targetId'] as String? ?? json['id'] as String,
      action: json['action'] as String? ?? 'removed',
      reason: json['reason'] as String? ?? '',
      contentExcerpt: json['contentExcerpt'] as String?,
      createdAt: DateTime.tryParse(json['createdAt'] as String? ?? '') ??
          DateTime.now(),
      appealStatus: json['appealStatus'] as String? ?? 'none',
    );
  }
}

class ReportHistoryPage {
  const ReportHistoryPage({required this.reports});

  final List<ReportHistoryItem> reports;

  factory ReportHistoryPage.fromJson(Map<String, Object?> json) {
    final rawReports = json['reports'] as List<Object?>? ?? [];
    return ReportHistoryPage(
      reports: rawReports
          .cast<Map<String, Object?>>()
          .map(ReportHistoryItem.fromJson)
          .toList(),
    );
  }
}

class ReportHistoryItem {
  const ReportHistoryItem({
    required this.id,
    required this.targetType,
    required this.targetId,
    required this.reason,
    required this.status,
    required this.publicStatus,
    required this.createdAt,
    this.targetPreview,
    this.publicReason,
  });

  final String id;
  final String targetType;
  final String targetId;
  final String reason;
  final String status;
  final String publicStatus;
  final String? targetPreview;
  final String? publicReason;
  final DateTime createdAt;

  factory ReportHistoryItem.fromJson(Map<String, Object?> json) {
    return ReportHistoryItem(
      id: json['id'] as String,
      targetType: json['targetType'] as String? ?? 'post',
      targetId: json['targetId'] as String? ?? '',
      reason: json['reason'] as String? ?? 'other',
      status: json['status'] as String? ?? 'pending',
      publicStatus: json['publicStatus'] as String? ?? 'alındı',
      targetPreview: json['targetPreview'] as String?,
      publicReason: json['publicReason'] as String?,
      createdAt: DateTime.tryParse(json['createdAt'] as String? ?? '') ??
          DateTime.now(),
    );
  }
}
