import '../../core/api/api_client.dart';
import '../../core/api/api_endpoints.dart';

class AdminService {
  AdminService({required ApiClient apiClient}) : _client = apiClient;

  final ApiClient _client;
  String? _adminToken;

  bool get isLoggedIn => _adminToken != null;
  String? get token => _adminToken;

  Future<void> login({
    required String email,
    required String password,
    required String totpCode,
  }) async {
    final json = await _client.postJson<Map<String, Object?>>(
      ApiEndpoints.adminLogin,
      body: {'email': email, 'password': password, 'totpCode': totpCode},
      adminToken: null,
    );
    _adminToken = json['accessToken'] as String?;
    if (_adminToken == null) throw Exception('Token alınamadı');
  }

  void logout() => _adminToken = null;

  // ── Moderasyon Kuyruğu ───────────────────────────────────────────────────

  Future<AdminQueueResult> fetchQueue({
    String? status,
    String? priority,
    int page = 1,
  }) async {
    final json = await _client.getJson<Map<String, Object?>>(
      ApiEndpoints.adminModerationQueue,
      queryParams: {
        if (status != null) 'status': status,
        if (priority != null) 'priority': priority,
        'page': page.toString(),
      },
      adminToken: _adminToken,
    );
    return AdminQueueResult.fromJson(json);
  }

  Future<void> moderationAction({
    required String targetType,
    required String targetId,
    required String action,
  }) async {
    await _client.postJson<Map<String, Object?>>(
      '/api/v1/admin/moderation/$targetType/$targetId/$action',
      body: {},
      adminToken: _adminToken,
    );
  }

  // ── Raporlar ─────────────────────────────────────────────────────────────

  Future<AdminReportsResult> fetchReports({
    String? status,
    int page = 1,
  }) async {
    final json = await _client.getJson<Map<String, Object?>>(
      ApiEndpoints.adminReports,
      queryParams: {
        if (status != null) 'status': status,
        'page': page.toString(),
      },
      adminToken: _adminToken,
    );
    return AdminReportsResult.fromJson(json);
  }

  Future<void> reportAction({
    required String reportId,
    required String action,
    String? note,
  }) async {
    await _client.postJson<Map<String, Object?>>(
      '/api/v1/admin/reports/$reportId/action',
      body: {'action': action, if (note != null) 'note': note},
      adminToken: _adminToken,
    );
  }

  // ── Kullanıcılar ─────────────────────────────────────────────────────────

  Future<AdminUsersResult> fetchUsers({
    String? search,
    bool? banned,
    int page = 1,
  }) async {
    final json = await _client.getJson<Map<String, Object?>>(
      ApiEndpoints.adminUsers,
      queryParams: {
        if (search != null) 'search': search,
        if (banned != null) 'banned': banned.toString(),
        'page': page.toString(),
      },
      adminToken: _adminToken,
    );
    return AdminUsersResult.fromJson(json);
  }

  Future<void> banUser(String userId, {required String reason}) async {
    await _client.postJson<Map<String, Object?>>(
      '/api/v1/admin/users/$userId/ban',
      body: {'reason': reason},
      adminToken: _adminToken,
    );
  }

  Future<void> unbanUser(String userId) async {
    await _client.postJson<Map<String, Object?>>(
      '/api/v1/admin/users/$userId/unban',
      body: {},
      adminToken: _adminToken,
    );
  }

  Future<void> warnUser(String userId, {required String message}) async {
    await _client.postJson<Map<String, Object?>>(
      '/api/v1/admin/users/$userId/warn',
      body: {'message': message},
      adminToken: _adminToken,
    );
  }

  // ── Postlar ──────────────────────────────────────────────────────────────

  Future<AdminPostsResult> fetchPosts({
    String? search,
    String? status,
    int page = 1,
  }) async {
    final json = await _client.getJson<Map<String, Object?>>(
      ApiEndpoints.adminPosts,
      queryParams: {
        if (search != null) 'search': search,
        if (status != null) 'status': status,
        'page': page.toString(),
      },
      adminToken: _adminToken,
    );
    return AdminPostsResult.fromJson(json);
  }

  Future<void> deletePost(String postId) async {
    await _client.deleteJson('/api/v1/admin/posts/$postId',
        adminToken: _adminToken);
  }

  // ── Cihazlar ─────────────────────────────────────────────────────────────

  Future<AdminDevicesResult> fetchDevices({
    String? search,
    bool? banned,
    int page = 1,
  }) async {
    final json = await _client.getJson<Map<String, Object?>>(
      ApiEndpoints.adminDevices,
      queryParams: {
        if (search != null) 'search': search,
        if (banned != null) 'banned': banned.toString(),
        'page': page.toString(),
      },
      adminToken: _adminToken,
    );
    return AdminDevicesResult.fromJson(json);
  }

  Future<void> banDevice(
    String deviceId, {
    required String reason,
    required String type,
    int? durationDays,
  }) async {
    await _client.postJson<Map<String, Object?>>(
      '/api/v1/admin/devices/$deviceId/ban',
      body: {
        'reason': reason,
        'type': type,
        if (durationDays != null) 'durationDays': durationDays,
      },
      adminToken: _adminToken,
    );
  }

  Future<void> unbanDevice(String deviceId) async {
    await _client.postJson<Map<String, Object?>>(
      '/api/v1/admin/devices/$deviceId/unban',
      body: {},
      adminToken: _adminToken,
    );
  }
}

// ── Modeller ─────────────────────────────────────────────────────────────────

class AdminQueueItem {
  const AdminQueueItem({
    required this.id,
    required this.targetType,
    required this.targetId,
    required this.content,
    required this.priority,
    required this.status,
    required this.reportCount,
    required this.createdAt,
    this.imageUrl,
    this.aiScore,
  });

  final String id;
  final String targetType;
  final String targetId;
  final String content;
  final String priority;
  final String status;
  final int reportCount;
  final DateTime createdAt;
  final String? imageUrl;
  final double? aiScore;

  factory AdminQueueItem.fromJson(Map<String, Object?> j) => AdminQueueItem(
        id: j['id'] as String,
        targetType: j['targetType'] as String,
        targetId: j['targetId'] as String,
        content: j['content'] as String? ?? '',
        priority: j['priority'] as String? ?? 'low',
        status: j['status'] as String? ?? 'pending',
        reportCount: j['reportCount'] as int? ?? 0,
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
            DateTime.now(),
        imageUrl: j['imageUrl'] as String?,
        aiScore: (j['aiScore'] as num?)?.toDouble(),
      );
}

class AdminQueueResult {
  const AdminQueueResult({required this.items, required this.total});
  final List<AdminQueueItem> items;
  final int total;

  factory AdminQueueResult.fromJson(Map<String, Object?> j) {
    final list = (j['items'] as List<Object?>? ?? [])
        .cast<Map<String, Object?>>()
        .map(AdminQueueItem.fromJson)
        .toList();
    return AdminQueueResult(items: list, total: j['total'] as int? ?? 0);
  }
}

class AdminReport {
  const AdminReport({
    required this.id,
    required this.reason,
    required this.targetType,
    required this.targetId,
    required this.content,
    required this.status,
    required this.createdAt,
    this.note,
  });

  final String id;
  final String reason;
  final String targetType;
  final String targetId;
  final String content;
  final String status;
  final DateTime createdAt;
  final String? note;

  factory AdminReport.fromJson(Map<String, Object?> j) => AdminReport(
        id: j['id'] as String,
        reason: j['reason'] as String? ?? '',
        targetType: j['targetType'] as String? ?? '',
        targetId: j['targetId'] as String? ?? '',
        content: j['content'] as String? ?? '',
        status: j['status'] as String? ?? 'pending',
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
            DateTime.now(),
        note: j['note'] as String?,
      );
}

class AdminReportsResult {
  const AdminReportsResult({required this.items, required this.total});
  final List<AdminReport> items;
  final int total;

  factory AdminReportsResult.fromJson(Map<String, Object?> j) {
    final list = (j['items'] as List<Object?>? ?? [])
        .cast<Map<String, Object?>>()
        .map(AdminReport.fromJson)
        .toList();
    return AdminReportsResult(items: list, total: j['total'] as int? ?? 0);
  }
}

class AdminUser {
  const AdminUser({
    required this.id,
    required this.username,
    required this.email,
    required this.createdAt,
    required this.isBanned,
    required this.postCount,
    required this.commentCount,
  });

  final String id;
  final String username;
  final String email;
  final DateTime createdAt;
  final bool isBanned;
  final int postCount;
  final int commentCount;

  factory AdminUser.fromJson(Map<String, Object?> j) => AdminUser(
        id: j['id'] as String,
        username: j['username'] as String? ?? '',
        email: j['email'] as String? ?? '',
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
            DateTime.now(),
        isBanned: j['isBanned'] as bool? ?? false,
        postCount: j['postCount'] as int? ?? 0,
        commentCount: j['commentCount'] as int? ?? 0,
      );
}

class AdminUsersResult {
  const AdminUsersResult({required this.items, required this.total});
  final List<AdminUser> items;
  final int total;

  factory AdminUsersResult.fromJson(Map<String, Object?> j) {
    final list = (j['items'] as List<Object?>? ?? [])
        .cast<Map<String, Object?>>()
        .map(AdminUser.fromJson)
        .toList();
    return AdminUsersResult(items: list, total: j['total'] as int? ?? 0);
  }
}

class AdminPost {
  const AdminPost({
    required this.id,
    required this.content,
    required this.status,
    required this.createdAt,
    required this.voteCount,
    required this.reportCount,
    this.username,
    this.imageUrl,
  });

  final String id;
  final String content;
  final String status;
  final DateTime createdAt;
  final int voteCount;
  final int reportCount;
  final String? username;
  final String? imageUrl;

  factory AdminPost.fromJson(Map<String, Object?> j) => AdminPost(
        id: j['id'] as String,
        content: j['content'] as String? ?? '',
        status: j['status'] as String? ?? 'active',
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
            DateTime.now(),
        voteCount: ((j['voteCountHakli'] as int? ?? 0) +
            (j['voteCountHaksiz'] as int? ?? 0)),
        reportCount: j['reportCount'] as int? ?? 0,
        username: j['username'] as String?,
        imageUrl: j['imageUrl'] as String?,
      );
}

class AdminPostsResult {
  const AdminPostsResult({required this.items, required this.total});
  final List<AdminPost> items;
  final int total;

  factory AdminPostsResult.fromJson(Map<String, Object?> j) {
    final list = (j['items'] as List<Object?>? ?? [])
        .cast<Map<String, Object?>>()
        .map(AdminPost.fromJson)
        .toList();
    return AdminPostsResult(items: list, total: j['total'] as int? ?? 0);
  }
}

class AdminDevice {
  const AdminDevice({
    required this.id,
    required this.createdAt,
    required this.isBanned,
    required this.postCount,
    required this.reportCount,
    this.banReason,
    this.platform,
  });

  final String id;
  final DateTime createdAt;
  final bool isBanned;
  final int postCount;
  final int reportCount;
  final String? banReason;
  final String? platform;

  factory AdminDevice.fromJson(Map<String, Object?> j) => AdminDevice(
        id: j['id'] as String,
        createdAt: DateTime.tryParse(j['createdAt'] as String? ?? '') ??
            DateTime.now(),
        isBanned: j['isBanned'] as bool? ?? false,
        postCount: j['postCount'] as int? ?? 0,
        reportCount: j['reportCount'] as int? ?? 0,
        banReason: j['banReason'] as String?,
        platform: j['platform'] as String?,
      );
}

class AdminDevicesResult {
  const AdminDevicesResult({required this.items, required this.total});
  final List<AdminDevice> items;
  final int total;

  factory AdminDevicesResult.fromJson(Map<String, Object?> j) {
    final list = (j['items'] as List<Object?>? ?? [])
        .cast<Map<String, Object?>>()
        .map(AdminDevice.fromJson)
        .toList();
    return AdminDevicesResult(items: list, total: j['total'] as int? ?? 0);
  }
}
