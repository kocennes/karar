import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../app_services.dart';
import '../providers.dart';

class UserPreferences {
  const UserPreferences({
    this.verdictMilestone = true,
    this.newComment = true,
    this.commentReply = true,
    this.notifyOnMention = true,
    this.postModeration = true,
    this.notifyOnTrend = false,
    this.notifyOnDigest = false,
    this.pushEnabled = true,
    this.soundEnabled = true,
    this.emailNewsletter = false,
    this.silentHoursEnabled = false,
    this.silentStart = '22:00',
    this.silentEnd = '08:00',
    this.showVotesOnProfile = true,
    this.showKarmaToOthers = true,
    this.analyticsEnabled = true,
  });

  final bool verdictMilestone;
  final bool newComment;
  final bool commentReply;
  final bool notifyOnMention;
  final bool postModeration;
  final bool notifyOnTrend;
  final bool notifyOnDigest;
  final bool pushEnabled;
  final bool soundEnabled;
  final bool emailNewsletter;
  final bool silentHoursEnabled;
  final String silentStart;
  final String silentEnd;
  final bool showVotesOnProfile;
  final bool showKarmaToOthers;
  final bool analyticsEnabled;

  factory UserPreferences.fromJson(Map<String, dynamic> json) {
    String? qs = json['quietHoursStart'] as String?;
    String? qe = json['quietHoursEnd'] as String?;
    return UserPreferences(
      verdictMilestone: json['notifyOnVerdict'] as bool? ?? true,
      newComment: json['notifyOnComment'] as bool? ?? true,
      commentReply: json['notifyOnReply'] as bool? ?? true,
      notifyOnMention: json['notifyOnMention'] as bool? ?? true,
      postModeration: json['notifyOnPostStatus'] as bool? ?? true,
      notifyOnTrend: json['notifyOnTrend'] as bool? ?? false,
      notifyOnDigest: json['notifyOnDigest'] as bool? ?? false,
      pushEnabled: json['pushEnabled'] as bool? ?? true,
      soundEnabled: json['soundEnabled'] as bool? ?? true,
      emailNewsletter: json['emailWeeklySummary'] as bool? ?? false,
      silentHoursEnabled: qs != null && qe != null,
      silentStart: qs ?? '22:00',
      silentEnd: qe ?? '08:00',
      showVotesOnProfile: json['showVotesOnProfile'] as bool? ?? true,
      showKarmaToOthers: json['showKarmaToOthers'] as bool? ?? true,
      analyticsEnabled: true,
    );
  }

  UserPreferences copyWith({
    bool? verdictMilestone,
    bool? newComment,
    bool? commentReply,
    bool? notifyOnMention,
    bool? postModeration,
    bool? notifyOnTrend,
    bool? notifyOnDigest,
    bool? pushEnabled,
    bool? soundEnabled,
    bool? emailNewsletter,
    bool? silentHoursEnabled,
    String? silentStart,
    String? silentEnd,
    bool? showVotesOnProfile,
    bool? showKarmaToOthers,
    bool? analyticsEnabled,
  }) {
    return UserPreferences(
      verdictMilestone: verdictMilestone ?? this.verdictMilestone,
      newComment: newComment ?? this.newComment,
      commentReply: commentReply ?? this.commentReply,
      notifyOnMention: notifyOnMention ?? this.notifyOnMention,
      postModeration: postModeration ?? this.postModeration,
      notifyOnTrend: notifyOnTrend ?? this.notifyOnTrend,
      notifyOnDigest: notifyOnDigest ?? this.notifyOnDigest,
      pushEnabled: pushEnabled ?? this.pushEnabled,
      soundEnabled: soundEnabled ?? this.soundEnabled,
      emailNewsletter: emailNewsletter ?? this.emailNewsletter,
      silentHoursEnabled: silentHoursEnabled ?? this.silentHoursEnabled,
      silentStart: silentStart ?? this.silentStart,
      silentEnd: silentEnd ?? this.silentEnd,
      showVotesOnProfile: showVotesOnProfile ?? this.showVotesOnProfile,
      showKarmaToOthers: showKarmaToOthers ?? this.showKarmaToOthers,
      analyticsEnabled: analyticsEnabled ?? this.analyticsEnabled,
    );
  }

  Map<String, dynamic> toJson() => {
        'notifyOnVerdict': verdictMilestone,
        'notifyOnComment': newComment,
        'notifyOnReply': commentReply,
        'notifyOnMention': notifyOnMention,
        'notifyOnPostStatus': postModeration,
        'notifyOnTrend': notifyOnTrend,
        'notifyOnDigest': notifyOnDigest,
        'pushEnabled': pushEnabled,
        'soundEnabled': soundEnabled,
        'emailWeeklySummary': emailNewsletter,
        'quietHoursStart': silentHoursEnabled ? silentStart : null,
        'quietHoursEnd': silentHoursEnabled ? silentEnd : null,
        'showVotesOnProfile': showVotesOnProfile,
        'showKarmaToOthers': showKarmaToOthers,
      };
}

class UserPreferencesNotifier extends Notifier<UserPreferences> {
  @override
  UserPreferences build() {
    Future.microtask(_load);
    return const UserPreferences();
  }

  static const _kPrefix = 'pref_';

  Future<void> _load() async {
    final prefs = await SharedPreferences.getInstance();

    // If user is logged in, load from backend (authoritative)
    if (AppRuntime.useRemoteApi && ref.read(currentUserProvider) != null) {
      try {
        final json = await ref.read(authServiceProvider).getNotificationPreferences();
        final fromServer = UserPreferences.fromJson(json);
        state = fromServer.copyWith(
          analyticsEnabled: prefs.getBool('${_kPrefix}analytics') ?? true,
        );
        return;
      } catch (_) {
        // Fall through to local cache
      }
    }

    state = UserPreferences(
      verdictMilestone: prefs.getBool('${_kPrefix}verdict') ?? true,
      newComment: prefs.getBool('${_kPrefix}comment') ?? true,
      commentReply: prefs.getBool('${_kPrefix}reply') ?? true,
      notifyOnMention: prefs.getBool('${_kPrefix}mention') ?? true,
      postModeration: prefs.getBool('${_kPrefix}mod') ?? true,
      notifyOnTrend: prefs.getBool('${_kPrefix}trend') ?? false,
      notifyOnDigest: prefs.getBool('${_kPrefix}digest') ?? false,
      pushEnabled: prefs.getBool('${_kPrefix}push') ?? true,
      soundEnabled: prefs.getBool('${_kPrefix}sound') ?? true,
      emailNewsletter: prefs.getBool('${_kPrefix}email') ?? false,
      silentHoursEnabled: prefs.getBool('${_kPrefix}silent_on') ?? false,
      silentStart: prefs.getString('${_kPrefix}silent_start') ?? '22:00',
      silentEnd: prefs.getString('${_kPrefix}silent_end') ?? '08:00',
      showVotesOnProfile: prefs.getBool('${_kPrefix}v_vis') ?? true,
      showKarmaToOthers: prefs.getBool('${_kPrefix}k_vis') ?? true,
      analyticsEnabled: prefs.getBool('${_kPrefix}analytics') ?? true,
    );
  }

  Future<void> update(UserPreferences Function(UserPreferences) updateFn) async {
    final newState = updateFn(state);
    final previousAnalytics = state.analyticsEnabled;
    state = newState;

    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool('${_kPrefix}verdict', state.verdictMilestone);
    await prefs.setBool('${_kPrefix}comment', state.newComment);
    await prefs.setBool('${_kPrefix}reply', state.commentReply);
    await prefs.setBool('${_kPrefix}mention', state.notifyOnMention);
    await prefs.setBool('${_kPrefix}mod', state.postModeration);
    await prefs.setBool('${_kPrefix}trend', state.notifyOnTrend);
    await prefs.setBool('${_kPrefix}digest', state.notifyOnDigest);
    await prefs.setBool('${_kPrefix}push', state.pushEnabled);
    await prefs.setBool('${_kPrefix}sound', state.soundEnabled);
    await prefs.setBool('${_kPrefix}email', state.emailNewsletter);
    await prefs.setBool('${_kPrefix}silent_on', state.silentHoursEnabled);
    await prefs.setString('${_kPrefix}silent_start', state.silentStart);
    await prefs.setString('${_kPrefix}silent_end', state.silentEnd);
    await prefs.setBool('${_kPrefix}v_vis', state.showVotesOnProfile);
    await prefs.setBool('${_kPrefix}k_vis', state.showKarmaToOthers);
    await prefs.setBool('${_kPrefix}analytics', state.analyticsEnabled);

    if (previousAnalytics != state.analyticsEnabled) {
      ref.read(analyticsServiceProvider).setAnalyticsEnabled(state.analyticsEnabled);
    }

    if (AppRuntime.useRemoteApi && ref.read(currentUserProvider) != null) {
      _syncToBackend();
    }
  }

  Future<void> _syncToBackend() async {
    try {
      await ref.read(authServiceProvider).updatePreferences(state.toJson());
    } catch (_) {}
  }
}

final userPreferencesProvider =
    NotifierProvider<UserPreferencesNotifier, UserPreferences>(
  UserPreferencesNotifier.new,
);
