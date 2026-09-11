<script lang="ts">
  import "../app.css";
  import DialogModal from "$lib/components/DialogModal.svelte";
  import { dialogManager } from "$lib/dialog.svelte";
  import { onMount } from "svelte";

  let { children } = $props();

  onMount(() => {
    // Intercept native browser alerts to use Gemini Desktop themed modal
    window.alert = (msg?: any) => {
      dialogManager.alert(String(msg ?? ""));
    };
  });
</script>

{@render children()}
<DialogModal />
