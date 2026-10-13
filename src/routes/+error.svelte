<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import MainApp from "./+page.svelte";

  onMount(() => {
    try {
      goto("/", { replaceState: true });
    } catch (e) {}
  });
</script>

<MainApp />
