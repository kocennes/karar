class NotificationItem {
  const NotificationItem({
    required this.id,
    required this.type,
    required this.title,
    required this.body,
    required this.isRead,
    required this.createdAt,
    this.postId,
    this.deepLink,
    this.ruleViolated,
  });

  final String id;
  final String type;
  final String title;
  final String body;
  final bool isRead;
  final DateTime createdAt;
  final String? postId;
  final String? deepLink;
  final String? ruleViolated;

  NotificationItem copyWith({bool? isRead}) => NotificationItem(
        id: id,
        type: type,
        title: title,
        body: body,
        isRead: isRead ?? this.isRead,
        createdAt: createdAt,
        postId: postId,
        deepLink: deepLink,
        ruleViolated: ruleViolated,
      );
}
