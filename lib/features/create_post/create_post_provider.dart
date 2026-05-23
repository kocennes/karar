import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

import '../../core/api/api_exception.dart';
import '../../core/app_services.dart';
import '../../core/providers.dart';
import '../../shared/widgets/rate_limit_ui.dart';
import '../feed/feed_provider.dart';

class CreatePostState {
  const CreatePostState({
    this.selectedCategoryId,
    this.images = const [],
    this.isLoading = false,
    this.error,
    this.errorCode,
    this.imageError,
    this.imagePermissionDenied = false,
    this.imageUploadFailed = false,
    this.tags = const [],
    this.pollOptions = const [],
    this.isUnlisted = false,
  });

  final int? selectedCategoryId;
  final List<XFile> images;
  final bool isLoading;
  final String? error;
  final String? errorCode;
  final String? imageError;
  final bool imagePermissionDenied;
  final bool imageUploadFailed;
  final List<String> tags;
  final List<String> pollOptions;
  final bool isUnlisted;

  static const int maxTags = 3;
  static const int maxPollOptions = 4;
  static const int minPollOptions = 2;
  static const int maxImages = 1;

  CreatePostState copyWith({
    int? selectedCategoryId,
    bool clearCategoryId = false,
    List<XFile>? images,
    bool clearImages = false,
    bool? isLoading,
    String? error,
    String? errorCode,
    bool clearError = false,
    String? imageError,
    bool clearImageError = false,
    bool? imagePermissionDenied,
    bool? imageUploadFailed,
    List<String>? tags,
    List<String>? pollOptions,
    bool? isUnlisted,
  }) =>
      CreatePostState(
        selectedCategoryId: clearCategoryId
            ? null
            : (selectedCategoryId ?? this.selectedCategoryId),
        images: clearImages ? [] : (images ?? this.images),
        isLoading: isLoading ?? this.isLoading,
        error: clearError ? null : (error ?? this.error),
        errorCode: clearError ? null : (errorCode ?? this.errorCode),
        imageError: clearImageError ? null : (imageError ?? this.imageError),
        imagePermissionDenied:
            imagePermissionDenied ?? this.imagePermissionDenied,
        imageUploadFailed: imageUploadFailed ?? this.imageUploadFailed,
        tags: tags ?? this.tags,
        pollOptions: pollOptions ?? this.pollOptions,
        isUnlisted: isUnlisted ?? this.isUnlisted,
      );

  bool get isDailyPostLimit =>
      errorCode == 'DAILY_POST_LIMIT' ||
      (errorCode == 'RATE_LIMIT_EXCEEDED' && error != null);
}

class CreatePostNotifier extends Notifier<CreatePostState> {
  @override
  CreatePostState build() => const CreatePostState();

  void selectCategory(int categoryId) {
    state = state.copyWith(selectedCategoryId: categoryId);
  }

  Future<void> pickImage() async {
    state = state.copyWith(
        clearImageError: true,
        imagePermissionDenied: false,
        imageUploadFailed: false);
    try {
      if (state.images.length >= CreatePostState.maxImages) return;

      final picker = ImagePicker();
      final picked = await picker.pickImage(
        source: ImageSource.gallery,
        maxWidth: 1280,
        maxHeight: 1280,
        imageQuality: 85,
      );
      if (picked == null) return;

      String? error;
      final ext = picked.name.split('.').last.toLowerCase();
      if (!{'jpg', 'jpeg', 'png', 'webp'}.contains(ext)) {
        error = 'Sadece JPG, PNG veya WebP yüklenebilir.';
      } else {
        final bytes = await picked.readAsBytes();
        if (bytes.length > 5 * 1024 * 1024) {
          error = 'Görsel 5 MB\'dan küçük olmalı.';
        }
      }

      state = state.copyWith(
        images: error == null ? [picked] : state.images,
        imageError: error,
        clearImageError: error == null,
      );
      if (error == null) {
        ref.read(analyticsServiceProvider).logCreatePostImageAdded();
      }
    } on PlatformException catch (e) {
      if (e.code == 'photo_access_denied' ||
          e.code == 'camera_access_denied' ||
          e.code == 'access_denied') {
        state = state.copyWith(imagePermissionDenied: true);
      }
    }
  }

  Future<void> setDroppedImage(XFile file) async {
    if (state.images.length >= CreatePostState.maxImages) return;

    final ext = file.name.split('.').last.toLowerCase();
    if (!{'jpg', 'jpeg', 'png', 'webp'}.contains(ext)) {
      state =
          state.copyWith(imageError: 'Sadece JPG, PNG veya WebP yüklenebilir.');
      return;
    }

    final bytes = await file.readAsBytes();
    if (bytes.length > 5 * 1024 * 1024) {
      state = state.copyWith(imageError: 'Görseller 5 MB\'dan küçük olmalı.');
      return;
    }

    state = state.copyWith(
      images: [...state.images, file],
      clearImageError: true,
      imagePermissionDenied: false,
      imageUploadFailed: false,
    );
    ref.read(analyticsServiceProvider).logCreatePostImageAdded();
  }

  void setImageError(String error) {
    state = state.copyWith(imageError: error);
  }

  void removeImage(int index) {
    final images = List<XFile>.from(state.images);
    if (index >= 0 && index < images.length) {
      images.removeAt(index);
      state = state.copyWith(images: images, imageUploadFailed: false);
    }
  }

  void clearError() {
    state = state.copyWith(clearError: true);
  }

  void addTag(String raw) {
    final tag = raw.trim().replaceAll('#', '').toLowerCase();
    if (tag.isEmpty || tag.length > 20) return;
    if (state.tags.contains(tag)) return;
    if (state.tags.length >= CreatePostState.maxTags) return;
    state = state.copyWith(tags: [...state.tags, tag]);
  }

  void removeTag(String tag) {
    state = state.copyWith(tags: state.tags.where((t) => t != tag).toList());
  }

  void addPollOption(String text) {
    if (text.trim().isEmpty) return;
    if (state.pollOptions.length >= CreatePostState.maxPollOptions) return;
    state = state.copyWith(pollOptions: [...state.pollOptions, text.trim()]);
  }

  void updatePollOption(int index, String text) {
    final options = List<String>.from(state.pollOptions);
    if (index < 0 || index >= options.length) return;
    options[index] = text.trim();
    state = state.copyWith(pollOptions: options);
  }

  void removePollOption(int index) {
    final options = List<String>.from(state.pollOptions);
    if (index < 0 || index >= options.length) return;
    options.removeAt(index);
    state = state.copyWith(pollOptions: options);
  }

  void clearPoll() {
    state = state.copyWith(pollOptions: []);
  }

  void setUnlisted(bool value) {
    state = state.copyWith(isUnlisted: value);
  }

  Future<bool> submit({
    required String title,
    required String content,
  }) async {
    final categoryId = state.selectedCategoryId;
    if (categoryId == null) {
      state = state.copyWith(error: 'Lütfen bir kategori seçin.');
      return false;
    }

    if (state.pollOptions.isNotEmpty &&
        state.pollOptions.length < CreatePostState.minPollOptions) {
      state = state.copyWith(error: 'Anket için en az 2 seçenek olmalı.');
      return false;
    }

    if (!AppRuntime.useRemoteApi) {
      state = const CreatePostState();
      return true;
    }

    state = state.copyWith(isLoading: true, clearError: true);

    ref.read(analyticsServiceProvider).logCreatePostSubmitted(
          category: categoryId.toString(),
          hasImage: state.images.isNotEmpty,
          contentLength: content.length,
        );

    try {
      await ref.read(postRepositoryProvider).createPost(
            title: title,
            content: content,
            categoryId: categoryId,
            images: state.images,
            tags: state.tags,
            pollOptions:
                state.pollOptions.isNotEmpty ? state.pollOptions : null,
            isUnlisted: state.isUnlisted,
          );

      // Log analytics
      ref.read(analyticsServiceProvider).logPostCreated(
            category: categoryId.toString(), // Simplified
            hasImage: state.images.isNotEmpty,
            contentLength: content.length,
            isRegistered: ref.read(authServiceProvider).isLoggedIn,
          );

      // Refresh feed so the new post appears
      ref.read(feedProvider.notifier).refresh();
      state = const CreatePostState();
      return true;
    } on ApiException catch (e) {
      state = state.copyWith(
        isLoading: false,
        error: RateLimitUi.messageFor(e, RateLimitedAction.post),
        errorCode: e.statusCode == 429 ? e.code : null,
        imageUploadFailed: state.images.isNotEmpty,
      );
      return false;
    } catch (_) {
      state = state.copyWith(
        isLoading: false,
        error: 'Gönderi paylaşılamadı. Tekrar dene.',
        imageUploadFailed: state.images.isNotEmpty,
      );
      return false;
    }
  }
}

final createPostProvider =
    NotifierProvider<CreatePostNotifier, CreatePostState>(
  CreatePostNotifier.new,
);
