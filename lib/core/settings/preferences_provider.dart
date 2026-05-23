import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../app_services.dart';
import '../providers.dart';

class UserPreferences {
  const UserPreferences({
    this.verdictMilestone = true,
    this.newComment = true,
    this.commentReply = true,
    this.postModeration = true,
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
  final bool postModeration;
  final bool emailNewsletter;
  final bool silentHoursEnabled;
  final String silentStart;
  final String silentEnd;
  final bool showVotesOnProfile;
  final bool showKarmaToOthers;
  final bool analyticsEnabled;

  UserPreferences copyWith({
    bool? verdictMilestone,
    bool? newComment,
    bool? commentReply,
    bool? postModeration,
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
      postModeration: postModeration ?? this.postModeration,
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
        'notifyOnModeration': postModeration,
        'emailNewsletter': emailNewsletter,
        'silentHoursEnabled': silentHoursEnabled,
        'silentStart': silentStart,
        'silentEnd': silentEnd,
        'showVotesOnProfile': showVotesOnProfile,
        'showKarmaToOthers': showKarmaToOthers,
      };
}

class UserPreferencesNotifier extends Notifier<UserPreferences> {
  @override
  UserPreferences build() {
    _load();
    return const UserPreferences();
  }

  static const _kPrefix = 'pref_';

  Future<void> _load() async {
    final prefs = await SharedPreferences.getInstance();
    state = UserPreferences(
      verdictMilestone: prefs.getBool('${_kPrefix}verdict') ?? true,
      newComment: prefs.getBool('${_kPrefix}comment') ?? true,
      commentReply: prefs.getBool('${_kPrefix}reply') ?? true,
      postModeration: prefs.getBool('${_kPrefix}mod') ?? true,
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
    await prefs.setBool('${_kPrefix}mod', state.postModeration);
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
