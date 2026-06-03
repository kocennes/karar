abstract final class ApiEndpoints {
  static const String devices = '/api/v1/devices/register';
  static const String fcmToken = '/api/v1/devices/fcm-token';

  static const String authRegister = '/api/v1/auth/register';
  static const String authVerifyEmail = '/api/v1/auth/verify-email';
  static const String authResendOtp = '/api/v1/auth/resend-otp';
  static const String authGoogle = '/api/v1/auth/google';
  static const String authLogin = '/api/v1/auth/login';
  static const String authRefresh = '/api/v1/auth/refresh';
  static const String authLogout = '/api/v1/auth/logout';
  static const String authCheckUsername = '/api/v1/auth/check-username';

  static const String categories = '/api/v1/categories';
  static const String posts = '/api/v1/posts';
  static const String postsDiscover = '/api/v1/posts/discover';
  static const String postsDiscoverFeed = '/api/v1/posts/discover/feed';
  static const String postsDiscoverEvents = '/api/v1/posts/discover/events';
  static const String postsToday = '/api/v1/posts/today';
  static const String postsWeeklyFeatured = '/api/v1/posts/weekly-featured';
  static const String trendTopics = '/api/v1/trends/topics';
  static const String search = '/api/v1/search';

  static String post(String id) => '/api/v1/posts/$id';
  static String postVote(String id) => '/api/v1/posts/$id/vote';
  static String postComments(String id) => '/api/v1/posts/$id/comments';
  static String postStoryImage(String id) =>
      '/api/v1/posts/$id/story-image.png';
  static String postSimilar(String id) => '/api/v1/posts/$id/similar';
  static String postStats(String id) => '/api/v1/posts/$id/stats';
  static String postView(String id) => '/api/v1/posts/$id/view';
  static String comment(String id) => '/api/v1/comments/$id';
  static String commentUpvote(String id) => '/api/v1/comments/$id/upvote';

  static const String reports = '/api/v1/reports';
  static const String feedback = '/api/v1/feedback';
  static const String notifications = '/api/v1/notifications';
  static const String notificationsReadAll = '/api/v1/notifications/read-all';
  static const String notificationsEvents = '/api/v1/notifications/events';

  static const String userMe = '/api/v1/users/me';
  static const String userMePosts = '/api/v1/users/me/posts';
  static const String userMePassword = '/api/v1/users/me/password';
  static const String usernameAvailability =
      '/api/v1/users/username-availability';

  static const String authForgotPassword = '/api/v1/auth/forgot-password';
  static const String authResetPassword = '/api/v1/auth/reset-password';

  static const String auth2faBackupCodes = '/api/v1/auth/2fa/backup-codes';
  static const String auth2faBackupCodesCount =
      '/api/v1/auth/2fa/backup-codes/count';

  static const String authChangeEmailRequest =
      '/api/v1/auth/change-email/request';
  static const String authChangeEmailConfirm =
      '/api/v1/auth/change-email/confirm';

  static const String authRecoverAccount = '/api/v1/auth/recover-account';

  static const String userMeComments = '/api/v1/users/me/comments';
  static const String userMeWeeklyStats = '/api/v1/users/me/weekly-stats';
  static String userPosts(String username) => '/api/v1/users/$username/posts';
  static String postPinComment(String postId) =>
      '/api/v1/posts/$postId/comments/pin';

  static const String userMeModerationHistory =
      '/api/v1/users/me/moderation-history';
  static const String userMeReports = '/api/v1/users/me/reports';
  static const String userMeModerationAppeals =
      '/api/v1/users/me/moderation-appeals';

  static const String authMigrateGuestData =
      '/api/v1/users/me/migrate-guest-data';
  static const String moderationCheck = '/api/v1/moderation/check';
  static const String moderationTransparency =
      '/api/v1/moderation/transparency';

  static const String growthEvents = '/api/v1/growth-events';
  static const String loopCompleted = '/api/v1/analytics/loop-completed';

  static const String policyStatus = '/api/v1/users/me/policy-status';
  static const String acceptPolicy = '/api/v1/users/me/accept-policy';
}
