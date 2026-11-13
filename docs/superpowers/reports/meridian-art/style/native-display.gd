extends SceneTree
func _initialize():
    var img = Image.load_from_file(OS.get_environment("MERIDIAN_MOCK"))
    assert(img.get_size() == Vector2i(1920, 1080))
    var view = TextureRect.new()
    view.texture = ImageTexture.create_from_image(img)
    view.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
    view.size = Vector2(1920, 1080)
    root.add_child(view)
    await create_timer(1).timeout
    await RenderingServer.frame_post_draw
    var error = root.get_texture().get_image().save_png(OS.get_environment("MERIDIAN_SCREENSHOT"))
    assert(error == OK)
    print("MERIDIAN MOCK REVIEW: native 1920x1080, nearest texture, screenshot saved")
    quit()
