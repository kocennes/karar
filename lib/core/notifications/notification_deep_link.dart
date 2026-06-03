class NotificationDeepLink {
  const NotificationDeepLink._();

  static String fromPayload(Map<String, dynamic> data) {
    return normalize(
      deepLink:
          _stringValue(data['deepLink']) ?? _stringValue(data['deeplink']),
      postId: _stringValue(data['postId']) ?? _stringValue(data['referenceId']),
      commentId: _stringValue(data['commentId']),
    );
  }

  static String fromNotificationItem({
    String? deepLink,
    String? postId,
  }) {
    return normalize(deepLink: deepLink, postId: postId);
  }

  static String normalize({
    String? deepLink,
    String? postId,
    String? commentId,
  }) {
    final destination = _routeFromDeepLink(deepLink) ??
        (postId == null || postId.trim().isEmpty
            ? '/notifications'
            : '/posts/${Uri.encodeComponent(postId.trim())}');

    return _withNotificationSource(destination, commentId: commentId);
  }

  static String? _routeFromDeepLink(String? deepLink) {
    final value = deepLink?.trim();
    if (value == null || value.isEmpty) return null;
    if (value.startsWith('/')) return value;

    final uri = Uri.tryParse(value);
    if (uri == null || uri.scheme != 'karar') return null;

    final path = uri.host.isNotEmpty ? '/${uri.host}${uri.path}' : uri.path;
    if (path.isEmpty || path == '/') return '/notifications';
    return Uri(
      path: path,
      queryParameters: uri.queryParameters.isEmpty ? null : uri.queryParameters,
    ).toString();
  }

  static String _withNotificationSource(
    String destination, {
    String? commentId,
  }) {
    if (!destination.startsWith('/posts/')) return destination;

    final uri = Uri.parse(destination);
    final query = Map<String, String>.from(uri.queryParameters);
    query['source'] = 'notification';

    final trimmedCommentId = commentId?.trim();
    if (trimmedCommentId != null && trimmedCommentId.isNotEmpty) {
      query.putIfAbsent('commentId', () => trimmedCommentId);
    }

    return uri.replace(queryParameters: query).toString();
  }

  static String? _stringValue(Object? value) =>
      value is String && value.trim().isNotEmpty ? value : null;
}
